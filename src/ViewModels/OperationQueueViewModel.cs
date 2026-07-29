using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// Runs copy/move operations in the background, surfacing each as a
/// <see cref="FileOperationViewModel"/> with live progress, speed and an estimate (PRD §6.3).
/// </summary>
/// <remarks>
/// <para>
/// Operations that touch different physical devices run at the same time; operations that share one
/// wait for each other. Two transfers on one spindle do not go twice as fast — they interleave and
/// both go slower than either would alone, which is the same reason the copy engine picks a
/// sequential strategy for a single mechanical disk. Two transfers between genuinely separate disks,
/// on the other hand, have no reason to queue.
/// </para>
/// <para>
/// A device that cannot be identified counts as clashing with everything. Guessing wrong in that
/// direction costs some parallelism; guessing wrong the other way costs throughput on the very
/// hardware the heuristic exists to protect.
/// </para>
/// </remarks>
public partial class OperationQueueViewModel : ViewModelBase
{
    private readonly IFileOperationService _operations;
    private readonly List<FileOperationViewModel> _pending = [];
    private readonly List<FileOperationViewModel> _running = [];

    /// <summary>
    /// Resolves destination collisions. Defaults to auto-rename so a background transfer never
    /// blocks; the view can replace it with a UI dialog (PRD §6.3 conflict resolution).
    /// </summary>
    public Func<ConflictRequest, ConflictResolution> ConflictResolver { get; set; } =
        _ => ConflictResolution.Rename;

    /// <summary>Raised on the calling context after each operation completes (for auto-refresh).</summary>
    public event EventHandler<FileOperationViewModel>? OperationCompleted;

    /// <summary>Resolves a path to the physical device it lives on; swapped out in tests.</summary>
    public Func<string, string> DeviceOf { get; set; } = DriveProfiler.PhysicalDeviceOf;

    /// <summary>
    /// The most operations to run at once, however many devices are free. A ceiling rather than a
    /// target: the device rule is what usually decides, and this only stops a drop of fifty folders
    /// across many mounts from starting fifty transfers.
    /// </summary>
    public int MaxConcurrent { get; set; } = 4;

    [ObservableProperty]
    private int _activeCount;

    public ObservableCollection<FileOperationViewModel> Operations { get; } = [];

    public OperationQueueViewModel(IFileOperationService operations)
    {
        _operations = operations;
    }

    public FileOperationViewModel Enqueue(FileOperationKind kind, IReadOnlyList<string> sources, string destinationDir)
    {
        var op = new FileOperationViewModel(kind, sources, destinationDir)
        {
            Devices = this.DevicesFor(sources, destinationDir),
        };

        Operations.Add(op);
        _pending.Add(op);
        ActiveCount++;
        this.StartWhatever();
        return op;
    }

    /// <summary>The devices an operation reads from and writes to — its sources' and its destination's.</summary>
    private HashSet<string> DevicesFor(IReadOnlyList<string> sources, string destinationDir)
    {
        var devices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
            devices.Add(this.DeviceOf(source) is { Length: > 0 } d ? d : string.Empty);

        devices.Add(this.DeviceOf(destinationDir) is { Length: > 0 } dest ? dest : string.Empty);
        return devices;
    }

    /// <summary>
    /// Starts every pending operation whose devices are all free, oldest first. Called whenever the
    /// picture changes — something enqueued, something finished.
    /// </summary>
    private void StartWhatever()
    {
        for (var i = 0; i < _pending.Count && _running.Count < this.MaxConcurrent;)
        {
            var op = _pending[i];
            if (this.Clashes(op))
            {
                ++i; // a later operation may still be free to go
                continue;
            }

            _pending.RemoveAt(i);
            _running.Add(op);
            _ = this.RunOneAsync(op);
        }
    }

    /// <summary>Whether an operation shares a device with anything already running.</summary>
    private bool Clashes(FileOperationViewModel op)
    {
        foreach (var running in _running)
            foreach (var device in op.Devices)
                // An unidentified device is the empty string, and it is deliberately equal to itself:
                // two operations we cannot place are assumed to be on the same disk.
                if (running.Devices.Contains(device))
                    return true;

        return false;
    }

    // Started on the UI thread; default (context-capturing) awaits resume there so VM state
    // and the Operations collection are only touched on the UI thread.
    private async Task RunOneAsync(FileOperationViewModel op)
    {
        op.Status = OperationStatus.Running;

        var clock = Stopwatch.StartNew();
        var rate = new TransferRate();
        op.Rate = rate;

        var progress = new Progress<OperationProgress>(p =>
        {
            op.Progress = p.Fraction;
            op.CurrentItem = p.CurrentItem;
            op.ItemProgress = p.ItemFraction;
            op.BytesTotal = p.BytesTotal;
            op.BytesDone = p.BytesDone;

            rate.Observe(p.BytesDone, clock.Elapsed.TotalSeconds);
            op.Speed = rate.Speed;
            op.AverageSpeed = rate.Average;
            op.Eta = rate.EtaFor(p.BytesTotal - p.BytesDone);
        });

        try
        {
            await _operations.TransferAsync(
                op.Sources, op.DestinationDir, op.Kind, progress, ConflictResolver, op.Cts.Token);
            op.MarkTerminal(OperationStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            op.MarkTerminal(OperationStatus.Cancelled);
        }
        catch (Exception ex)
        {
            op.MarkTerminal(OperationStatus.Failed, ex.Message);
        }
        finally
        {
            _running.Remove(op);
            ActiveCount = Math.Max(0, ActiveCount - 1);
            OperationCompleted?.Invoke(this, op);

            // Whatever was waiting on this operation's devices can go now.
            this.StartWhatever();
        }
    }
}
