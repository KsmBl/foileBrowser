using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystem;
    private readonly NavigationHistory _history = new();

    // Unfiltered/unsorted snapshot from the last successful load; the visible Entries
    // collection is derived from this whenever sort or hidden-visibility changes.
    private IReadOnlyList<FileSystemEntry> _rawEntries = [];

    // Guards against a slow load clobbering a newer one (PRD §6.12 async I/O).
    private CancellationTokenSource? _loadCts;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    // Two-way bound to the editable path bar; kept in sync with CurrentPath after each load
    // so typing a new path and pressing Enter navigates there (PRD §6.1 editable path bar).
    [ObservableProperty]
    private string _pathBarText = string.Empty;

    [ObservableProperty]
    private FileEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _showHidden;

    [ObservableProperty]
    private SortColumn _sortColumn = SortColumn.Name;

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Ascending;

    public ObservableCollection<FileEntryViewModel> Entries { get; } = [];

    /// <summary>Design-time constructor used by the XAML previewer only.</summary>
    public MainWindowViewModel() : this(new FileSystemService())
    {
    }

    public MainWindowViewModel(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Loads the initial directory (the user's home folder, falling back to the working
    /// directory). Called by the view once on open so the constructor stays synchronous and
    /// side-effect-free, keeping the view model unit-testable.
    /// </summary>
    public Task InitializeAsync()
    {
        var start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(start) || !_fileSystem.DirectoryExists(start))
            start = Directory.GetCurrentDirectory();

        return NavigateToAsync(start);
    }

    public bool CanGoBack => _history.CanGoBack;

    public bool CanGoForward => _history.CanGoForward;

    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && _fileSystem.GetParent(CurrentPath) is not null;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private Task GoBackAsync() => _history.GoBack() is { } path ? LoadAsync(path, record: false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private Task GoForwardAsync() => _history.GoForward() is { } path ? LoadAsync(path, record: false) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private Task GoUpAsync()
    {
        var parent = _fileSystem.GetParent(CurrentPath);
        return parent is null ? Task.CompletedTask : NavigateToAsync(parent);
    }

    [RelayCommand]
    private Task RefreshAsync() =>
        string.IsNullOrEmpty(CurrentPath) ? Task.CompletedTask : LoadAsync(CurrentPath, record: false);

    /// <summary>Opens the selected entry: descends into directories (files are handled by the OS later).</summary>
    [RelayCommand]
    private Task OpenAsync(FileEntryViewModel? item)
    {
        item ??= SelectedEntry;
        if (item is { IsDirectory: true })
            return NavigateToAsync(item.FullPath);

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void SortBy(SortColumn column)
    {
        if (SortColumn == column)
            SortDirection = SortDirection == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
        else
        {
            SortColumn = column;
            SortDirection = SortDirection.Ascending;
        }

        RebuildEntries();
    }

    [RelayCommand]
    private Task NavigatePathBarAsync() =>
        string.IsNullOrWhiteSpace(PathBarText) ? Task.CompletedTask : NavigateToAsync(PathBarText.Trim());

    public Task NavigateToAsync(string path) => LoadAsync(path, record: true);

    private async Task LoadAsync(string path, bool record)
    {
        // Cancel any in-flight load; last request wins.
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();
        var token = cts.Token;

        IsLoading = true;
        try
        {
            var entries = await _fileSystem.ListDirectoryAsync(path, token);
            if (token.IsCancellationRequested)
                return;

            _rawEntries = entries;
            CurrentPath = path;
            if (record)
                _history.Visit(path);

            RebuildEntries();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer navigation; leave the UI to that load.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or DirectoryNotFoundException or ArgumentException)
        {
            _rawEntries = [];
            RebuildEntries();
            StatusText = $"Cannot open “{path}”: {ex.Message}";
        }
        finally
        {
            if (_loadCts == cts)
                IsLoading = false;
            NotifyNavigationState();
        }
    }

    private void RebuildEntries()
    {
        var visible = ShowHidden ? _rawEntries : _rawEntries.Where(e => !e.IsHidden);
        var sorted = EntrySorter.Sort(visible, SortColumn, SortDirection);

        Entries.Clear();
        foreach (var entry in sorted)
            Entries.Add(new FileEntryViewModel(entry));

        var folders = sorted.Count(e => e.IsDirectory);
        StatusText = $"{sorted.Count} items ({folders} folders, {sorted.Count - folders} files)";
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
    }

    // Re-derive the visible list when the hidden-file toggle flips (PRD §6.1).
    partial void OnShowHiddenChanged(bool value) => RebuildEntries();

    // Mirror committed navigation back into the editable path bar.
    partial void OnCurrentPathChanged(string value) => PathBarText = value;
}
