using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoileBrowser.Models;

namespace FoileBrowser.ViewModels;

public enum OperationStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>A single queued copy/move shown in the operations panel (PRD §6.3 background queue).</summary>
public partial class FileOperationViewModel : ViewModelBase
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal readonly CancellationTokenSource Cts = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private OperationStatus _status = OperationStatus.Pending;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _currentItem = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public FileOperationKind Kind { get; }
    public IReadOnlyList<string> Sources { get; }
    public string DestinationDir { get; }

    /// <summary>Completes when the operation reaches a terminal state (never faults).</summary>
    public Task Completion => _completion.Task;

    public FileOperationViewModel(FileOperationKind kind, IReadOnlyList<string> sources, string destinationDir)
    {
        Kind = kind;
        Sources = sources;
        DestinationDir = destinationDir;
    }

    public bool IsActive => Status is OperationStatus.Pending or OperationStatus.Running;

    public string Description
    {
        get
        {
            var verb = Kind == FileOperationKind.Copy ? "Copy" : "Move";
            var what = Sources.Count == 1 ? Path.GetFileName(Sources[0].TrimEnd(Path.DirectorySeparatorChar))
                                          : $"{Sources.Count} items";
            return $"{verb} {what} → {Path.GetFileName(DestinationDir.TrimEnd(Path.DirectorySeparatorChar))}";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsActive)
            Cts.Cancel();
    }

    internal void MarkTerminal(OperationStatus status, string? error = null)
    {
        Status = status;
        ErrorMessage = error;
        if (status == OperationStatus.Completed)
            Progress = 1;
        _completion.TrySetResult();
    }
}
