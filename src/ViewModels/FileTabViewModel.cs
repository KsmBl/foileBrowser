using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// One browsing context: a single directory view with its own navigation history, sort
/// state and hidden-file toggle. A pane owns one or more of these as tabs (PRD §6.2).
/// </summary>
public partial class FileTabViewModel : ViewModelBase, IDisposable
{
    private readonly IFileSystemService _fileSystem;
    private readonly ISearchService _search;
    private readonly IShellService _shell;
    private readonly NavigationHistory _history = new();
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;

    private IReadOnlyList<FileSystemEntry> _rawEntries = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;

    // Auto-refresh: watch the current folder for external changes (PRD §6.12).
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    [ObservableProperty]
    private string _currentPath = string.Empty;

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

    // As-you-type filter over the current folder (PRD §6.4). Empty shows everything.
    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private SortColumn _sortColumn = SortColumn.Name;

    [ObservableProperty]
    private SortDirection _sortDirection = SortDirection.Ascending;

    // Recursive search state (PRD §6.4).
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _searchExtensions = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    // Filter to a single color tag when set (PRD §6.7 filterable tags).
    [ObservableProperty]
    private string? _tagFilter;

    /// <summary>Resolves the color tag for a path; set by the shell so entries show their tag dot.</summary>
    public Func<string, string?>? TagLookup { get; set; }

    /// <summary>Raised after any navigation completes so the owning pane can react (title, active path).</summary>
    public event EventHandler? Navigated;

    public ObservableCollection<FileEntryViewModel> Entries { get; } = [];

    public FileTabViewModel(IFileSystemService fileSystem, ISearchService? search = null, IShellService? shell = null)
    {
        _fileSystem = fileSystem;
        _search = search ?? new SearchService();
        _shell = shell ?? new ShellService();
    }

    /// <summary>Short label for the tab header — the current folder name, or the path for roots.</summary>
    public string Title
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentPath))
                return "New Tab";
            var name = Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? CurrentPath : name;
        }
    }

    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;
    public bool CanGoUp => !string.IsNullOrEmpty(CurrentPath) && _fileSystem.GetParent(CurrentPath) is not null;

    public Task InitializeAsync()
    {
        var start = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(start) || !_fileSystem.DirectoryExists(start))
            start = Directory.GetCurrentDirectory();
        return NavigateToAsync(start);
    }

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

    [RelayCommand]
    private Task OpenAsync(FileEntryViewModel? item)
    {
        item ??= SelectedEntry;
        if (item is null)
            return Task.CompletedTask;
        // Directories are entered in-place; files open with the OS default handler (PRD §6.9).
        return item.IsDirectory ? NavigateToAsync(item.FullPath) : _shell.OpenAsync(item.FullPath);
    }

    [RelayCommand]
    private void SortBy(SortColumn column)
    {
        if (SortColumn == column)
            SortDirection = SortDirection == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
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

    [RelayCommand]
    private async Task StartSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || string.IsNullOrEmpty(CurrentPath))
            return;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = _searchCts = new CancellationTokenSource();
        var token = cts.Token;

        IsSearching = true;
        IsLoading = true;
        Entries.Clear();

        var exts = ParseExtensions(SearchExtensions);
        var count = 0;
        try
        {
            await foreach (var hit in _search.SearchAsync(CurrentPath, SearchQuery.Trim(), exts, token))
            {
                Entries.Add(new FileEntryViewModel(hit, Path.GetDirectoryName(hit.FullPath), TagLookup?.Invoke(hit.FullPath)));
                count++;
                if ((count & 31) == 0)
                    StatusText = $"Searching… {count} matches";
            }
            StatusText = $"{count} matches for “{SearchQuery.Trim()}”";
        }
        catch (OperationCanceledException)
        {
            // Superseded or stopped by the user.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Search failed: {ex.Message}";
        }
        finally
        {
            if (_searchCts == cts)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private Task StopSearchAsync()
    {
        _searchCts?.Cancel();
        IsSearching = false;
        SearchQuery = string.Empty;
        return RefreshAsync();
    }

    private static string[] ParseExtensions(string text) =>
        text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public Task NavigateToAsync(string path) => LoadAsync(path, record: true);

    private async Task LoadAsync(string path, bool record)
    {
        // A fresh directory load ends any in-progress search.
        _searchCts?.Cancel();
        IsSearching = false;

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
            SetupWatcher(path);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer navigation.
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
            Navigated?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RebuildEntries()
    {
        // While searching, Entries holds streamed hits; don't overwrite them with the folder view.
        if (IsSearching)
            return;

        IEnumerable<FileSystemEntry> visible = ShowHidden ? _rawEntries : _rawEntries.Where(e => !e.IsHidden);
        if (!string.IsNullOrWhiteSpace(FilterText))
            visible = visible.Where(e => FuzzyMatcher.IsMatch(FilterText, e.Name));
        if (!string.IsNullOrEmpty(TagFilter))
            visible = visible.Where(e => string.Equals(TagLookup?.Invoke(e.FullPath), TagFilter, StringComparison.OrdinalIgnoreCase));
        var sorted = EntrySorter.Sort(visible, SortColumn, SortDirection);

        Entries.Clear();
        foreach (var entry in sorted)
            Entries.Add(new FileEntryViewModel(entry, tagColor: TagLookup?.Invoke(entry.FullPath)));

        var folders = sorted.Count(e => e.IsDirectory);
        StatusText = $"{sorted.Count} items ({folders} folders, {sorted.Count - folders} files)";
    }

    // ---- filesystem watcher (auto-refresh) ----

    private void SetupWatcher(string path)
    {
        DisposeWatcher();

        // Skip virtual/non-existent paths (also keeps unit tests, which use fake paths, watcher-free).
        if (!Directory.Exists(path))
            return;

        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Created += OnFsEvent;
            watcher.Deleted += OnFsEvent;
            watcher.Renamed += OnFsEvent;
            watcher.Changed += OnFsEvent;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Some filesystems/paths can't be watched; auto-refresh is simply unavailable there.
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // Coalesce bursts of events into a single refresh ~250 ms after the last one.
        _debounce ??= new Timer(_ => PostRefresh(), null, Timeout.Infinite, Timeout.Infinite);
        _debounce.Change(250, Timeout.Infinite);
    }

    private void PostRefresh()
    {
        void Refresh()
        {
            if (!IsSearching)
                _ = RefreshCommand.ExecuteAsync(null);
        }

        if (_sync is not null)
            _sync.Post(_ => Refresh(), null);
        else
            Refresh();
    }

    private void DisposeWatcher()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFsEvent;
            _watcher.Deleted -= OnFsEvent;
            _watcher.Renamed -= OnFsEvent;
            _watcher.Changed -= OnFsEvent;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public void Dispose()
    {
        DisposeWatcher();
        _debounce?.Dispose();
        _debounce = null;
        _loadCts?.Cancel();
        _searchCts?.Cancel();
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CanGoUp));
        OnPropertyChanged(nameof(Title));
        GoBackCommand.NotifyCanExecuteChanged();
        GoForwardCommand.NotifyCanExecuteChanged();
        GoUpCommand.NotifyCanExecuteChanged();
    }

    partial void OnShowHiddenChanged(bool value) => RebuildEntries();
    partial void OnFilterTextChanged(string value) => RebuildEntries();
    partial void OnTagFilterChanged(string? value) => RebuildEntries();
    partial void OnCurrentPathChanged(string value) => PathBarText = value;

    // Clear any active filter when navigating so a new folder shows in full.
    partial void OnCurrentPathChanging(string value)
    {
        if (!string.IsNullOrEmpty(FilterText))
            FilterText = string.Empty;
    }
}
