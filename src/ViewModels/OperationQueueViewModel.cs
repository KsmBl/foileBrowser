using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// Runs copy/move operations one at a time on a background thread, surfacing each as a
/// <see cref="FileOperationViewModel"/> with live progress (PRD §6.3 operation queue).
/// </summary>
public partial class OperationQueueViewModel : ViewModelBase
{
    private readonly IFileOperationService _operations;
    private readonly Queue<FileOperationViewModel> _pending = new();
    private bool _pumpRunning;

    /// <summary>
    /// Resolves destination collisions. Defaults to auto-rename so a background transfer never
    /// blocks; the view can replace it with a UI dialog (PRD §6.3 conflict resolution).
    /// </summary>
    public Func<ConflictRequest, ConflictResolution> ConflictResolver { get; set; } =
        _ => ConflictResolution.Rename;

    /// <summary>Raised on the calling context after each operation completes (for auto-refresh).</summary>
    public event EventHandler<FileOperationViewModel>? OperationCompleted;

    [ObservableProperty]
    private int _activeCount;

    public ObservableCollection<FileOperationViewModel> Operations { get; } = [];

    public OperationQueueViewModel(IFileOperationService operations)
    {
        _operations = operations;
    }

    public FileOperationViewModel Enqueue(FileOperationKind kind, IReadOnlyList<string> sources, string destinationDir)
    {
        var op = new FileOperationViewModel(kind, sources, destinationDir);
        Operations.Add(op);
        _pending.Enqueue(op);
        ActiveCount++;

        if (!_pumpRunning)
            _ = RunPumpAsync();

        return op;
    }

    // Started on the UI thread; default (context-capturing) awaits resume there so VM state
    // and the Operations collection are only touched on the UI thread.
    private async Task RunPumpAsync()
    {
        _pumpRunning = true;
        try
        {
            while (_pending.Count > 0)
            {
                var op = _pending.Dequeue();
                await RunOneAsync(op);
                ActiveCount = Math.Max(0, ActiveCount - 1);
                OperationCompleted?.Invoke(this, op);
            }
        }
        finally
        {
            _pumpRunning = false;
        }
    }

    private async Task RunOneAsync(FileOperationViewModel op)
    {
        op.Status = OperationStatus.Running;
        var progress = new Progress<OperationProgress>(p =>
        {
            op.Progress = p.Fraction;
            op.CurrentItem = p.CurrentItem;
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
    }
}
