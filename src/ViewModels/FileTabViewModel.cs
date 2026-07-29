using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoileBrowser.Docking;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// One browsing context: a single directory view with its own navigation history, sort state and
/// hidden-file toggle. Each tab is an <see cref="IDockable"/> in the docking layout, so tabs can be
/// dragged into a new pane, tabbed together or reordered (PRD §6.2).
/// </summary>
public partial class FileTabViewModel : ViewModelBase, IDockable, IDisposable
{
    /// <summary>The dockable tab header (folder name, or archive location).</summary>
    [ObservableProperty]
    private string _title = "New Tab";

    private readonly IFileSystemService _fileSystem;
    private readonly ISearchService _search;
    private readonly IShellService _shell;
    private readonly IArchiveService _archives;
    private readonly IDirectorySizeService _sizes;
    private readonly IMetadataService? _metadata;
    private readonly DisplayOptions _display;
    private readonly NavigationHistory _history = new();
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;

    private IReadOnlyList<FileSystemEntry> _rawEntries = [];
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _sizeCts;

    // Virtual archive browsing (PRD §6.11): while inside an archive, listings come from the archive
    // index instead of the filesystem, so nothing is extracted to temp except a single opened file.
    private string? _archivePath;
    private string _archiveInternal = "";
    private IReadOnlyList<ArchiveEntry> _archiveEntries = [];

    // Auto-refresh: watch the current folder for external changes (PRD §6.12).
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _pathBarText = string.Empty;

    /// <summary>When true the combined path bar shows an editable text entry instead of breadcrumbs (Thunar-style).</summary>
    [ObservableProperty]
    private bool _isEditingPath;

    [ObservableProperty]
    private FileEntryViewModel? _selectedEntry;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    private string _statusText = string.Empty;

    /// <summary>Summary of the current multi-selection (count + total size); empty when nothing is selected.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    private string _selectionStatus = string.Empty;

    /// <summary>The status-bar line: the selection summary when items are selected, else the folder totals.</summary>
    public string StatusLine => string.IsNullOrEmpty(SelectionStatus) ? StatusText : SelectionStatus;

    /// <summary>Every selected row (multi-select). <see cref="SelectedEntry"/> stays the primary/preview item.</summary>
    public IReadOnlyList<FileEntryViewModel> SelectedEntries { get; private set; } = [];

    /// <summary>
    /// Pushed from the view when the list selection changes; updates the selection summary.
    /// Raises a change notification because extending a selection (Ctrl/Shift-click) leaves
    /// <see cref="SelectedEntry"/> on the first row — without this the inspector would never learn
    /// that the selection grew, and would keep previewing that one item (PRD §6.5).
    /// </summary>
    public void SetSelection(IReadOnlyList<FileEntryViewModel> items)
    {
        var changed = !SelectedEntries.SequenceEqual(items);
        SelectedEntries = items;
        UpdateSelectionStatus();
        if (changed)
            OnPropertyChanged(nameof(SelectedEntries));
    }

    private void UpdateSelectionStatus()
    {
        var items = SelectedEntries;
        if (items.Count == 0)
        {
            SelectionStatus = string.Empty;
            return;
        }

        long total = 0;
        var partial = false;
        foreach (var item in items)
        {
            if (item.IsDirectory)
            {
                if (item.ComputedSize is { } computed) total += computed;
                else partial = true; // a folder whose size hasn't been counted yet
            }
            else
            {
                total += item.Entry.Size ?? 0;
            }
        }

        var size = ValueFormat.Size(total, _display.SizeUnit) + (partial ? "+" : string.Empty);
        SelectionStatus = $"{items.Count} selected · {size}";
    }

    [ObservableProperty]
    private bool _showHidden;

    /// <summary>Whether this pane shows its own left navigation tree (favorites/drives/partitions).</summary>
    [ObservableProperty]
    private bool _isSidebarVisible = true;

    /// <summary>Whether this pane shows thumbnails instead of rows (PRD §6.2).</summary>
    [ObservableProperty]
    private bool _isGallery;

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

    /// <summary>Raised with the path of a folder that actually listed, so the shell can remember it
    /// (PRD §6.1 recent folders). Unlike <see cref="Navigated"/>, a failed load does not raise it.</summary>
    public event EventHandler<string>? FolderOpened;

    /// <summary>Raised by Ctrl+F so this pane's view focuses its subtree-search box (PRD §6.4).</summary>
    public event EventHandler? SearchFocusRequested;

    /// <summary>Asks the view to focus this pane's search box (invoked by the shell's Find command).</summary>
    public void FocusSearch() => SearchFocusRequested?.Invoke(this, EventArgs.Empty);

    public ObservableCollection<FileEntryViewModel> Entries { get; } = [];

    /// <summary>Clickable path segments for the breadcrumb bar (PRD §6.1), root → current folder.</summary>
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    public FileTabViewModel(
        IFileSystemService fileSystem, ISearchService? search = null,
        IShellService? shell = null, IArchiveService? archives = null,
        IDirectorySizeService? sizes = null, DisplayOptions? display = null,
        IMetadataService? metadata = null)
    {
        _fileSystem = fileSystem;
        _search = search ?? new SearchService();
        _shell = shell ?? new ShellService();
        _archives = archives ?? new ArchiveService();
        _sizes = sizes ?? new DirectorySizeService();
        _metadata = metadata;
        _display = display ?? new DisplayOptions();
        UpdateTitle();
    }

    /// <summary>Resolves a metadata column's value lazily; refreshes the row when the value arrives.</summary>
    private string ResolveMetadata(FileEntryViewModel entry, string columnId) =>
        _metadata?.Get(entry.FullPath, columnId, () => Post(() => entry.CellVersion++)) ?? string.Empty;

    /// <summary>Wraps a real (on-disk) entry, wiring metadata-column resolution.</summary>
    private FileEntryViewModel NewEntry(FileSystemEntry entry, string? location = null)
    {
        var vm = new FileEntryViewModel(entry, location, TagLookup?.Invoke(entry.FullPath), _display);
        if (_metadata is not null)
            vm.Metadata = ResolveMetadata;
        return vm;
    }

    /// <summary>Re-renders every visible entry's size/date after a display-mode toggle (PRD §6.1/§6.2).</summary>
    public void RefreshDisplays()
    {
        foreach (var entry in Entries)
            entry.RefreshDisplay();
    }

    /// <summary>Updates the dockable tab header (Document.Title) — folder name, or archive location.</summary>
    private void UpdateTitle()
    {
        if (_archivePath is not null)
        {
            var name = Path.GetFileName(_archivePath);
            Title = _archiveInternal.Length > 0 ? $"{name}/{_archiveInternal}" : name;
            return;
        }
        if (string.IsNullOrEmpty(CurrentPath))
        {
            Title = "New Tab";
            return;
        }
        var folder = Path.GetFileName(CurrentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        Title = string.IsNullOrEmpty(folder) ? CurrentPath : folder;
    }

    public bool CanGoBack => _history.CanGoBack;
    public bool CanGoForward => _history.CanGoForward;
    public bool CanGoUp => _archivePath is not null
        || (!string.IsNullOrEmpty(CurrentPath) && _fileSystem.GetParent(CurrentPath) is not null);

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
        if (_archivePath is not null)
        {
            if (_archiveInternal.Length > 0)
            {
                var slash = _archiveInternal.LastIndexOf('/');
                _archiveInternal = slash > 0 ? _archiveInternal[..slash] : string.Empty;
                ShowArchiveDir();
                return Task.CompletedTask;
            }
            // At the archive root: leave the archive back to the folder that contains the file.
            var containing = Path.GetDirectoryName(_archivePath);
            return string.IsNullOrEmpty(containing) ? Task.CompletedTask : NavigateToAsync(containing);
        }

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
        // Inside an archive, entries are virtual — route folders/files through the archive index.
        if (_archivePath is not null)
            return OpenArchiveEntryAsync(item);
        if (item.IsDirectory)
            return NavigateToAsync(item.FullPath);
        // Archives are entered as virtual folders (PRD §6.11); other files open with the OS default.
        if (_archives.IsArchive(item.FullPath))
            return EnterArchiveAsync(item.FullPath);
        return _shell.OpenAsync(item.FullPath);
    }

    // ---- virtual archive browsing (PRD §6.11): list from the index, extract only on open ----

    /// <summary>Opens an archive and browses its contents virtually, without extracting to temp.</summary>
    private async Task EnterArchiveAsync(string archivePath)
    {
        IsLoading = true;
        StatusText = $"Opening {Path.GetFileName(archivePath)}…";
        try
        {
            _archiveEntries = await _archives.ListAsync(archivePath);
            _archivePath = archivePath;
            _archiveInternal = string.Empty;
            _history.Visit(archivePath);
            DisposeWatcher(); // virtual paths aren't watchable
            ShowArchiveDir();
        }
        catch (Exception ex)
        {
            // Third-party format readers can throw anything; never let a bad archive crash the app.
            _archivePath = null;
            StatusText = $"Cannot open archive: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task OpenArchiveEntryAsync(FileEntryViewModel item)
    {
        if (item.IsDirectory)
        {
            _archiveInternal = item.FullPath; // stored as the internal directory path
            ShowArchiveDir();
            return;
        }

        try
        {
            IsLoading = true;
            var dest = Path.Combine(
                Path.GetTempPath(), "foileBrowser", "entry-" + Guid.NewGuid().ToString("N")[..8], item.Name);
            await _archives.ExtractEntryAsync(_archivePath!, item.FullPath, dest);

            if (_archives.IsArchive(dest))
                await EnterArchiveAsync(dest); // nested archive: browse the single extracted file virtually
            else
                await _shell.OpenAsync(dest);
        }
        catch (Exception ex)
        {
            StatusText = $"Cannot open entry: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Entries larger than this aren't extracted just to preview them (PRD §6.5/§6.11).
    private const long MaxPreviewExtractBytes = 16L * 1024 * 1024;

    /// <summary>
    /// Resolves the entry the inspector/quick-preview should read: real entries pass through unchanged,
    /// while a file inside an open archive is streamed out to a temp file first so it can be previewed
    /// without leaving archive-browsing mode (PRD §6.5/§6.11). Returns null when there's nothing to
    /// preview (a virtual archive folder, or an entry too large to extract on a whim).
    /// </summary>
    public async Task<FileSystemEntry?> ResolvePreviewEntryAsync(FileEntryViewModel item, CancellationToken token)
    {
        if (_archivePath is null)
            return item.Entry;
        if (item.IsDirectory)
            return null;
        if (item.Entry.Size is > MaxPreviewExtractBytes)
            return null;

        try
        {
            var dest = Path.Combine(
                Path.GetTempPath(), "foileBrowser", "preview-" + Guid.NewGuid().ToString("N")[..8], item.Name);
            await _archives.ExtractEntryAsync(_archivePath, item.FullPath, dest, token);
            return item.Entry with { FullPath = dest };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A bad/oversized entry simply yields no preview rather than throwing.
            return null;
        }
    }

    /// <summary>Rebuilds the file list from the archive index for the current internal directory.</summary>
    private void ShowArchiveDir()
    {
        var prefix = _archiveInternal.Length == 0 ? string.Empty : _archiveInternal + "/";
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(string Name, ArchiveEntry Entry)>();

        foreach (var entry in _archiveEntries)
        {
            var norm = entry.Name.Replace('\\', '/').TrimStart('/');
            if (!norm.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var rest = norm[prefix.Length..].TrimEnd('/');
            if (rest.Length == 0)
                continue;

            var slash = rest.IndexOf('/');
            if (slash >= 0)
                dirs.Add(rest[..slash]);
            else if (entry.IsDirectory)
                dirs.Add(rest);
            else
                files.Add((rest, entry));
        }

        Entries.Clear();
        foreach (var dir in dirs)
            Entries.Add(new FileEntryViewModel(
                new FileSystemEntry { Name = dir, FullPath = prefix + dir, Kind = FileSystemEntryKind.Directory },
                display: _display));
        foreach (var (name, entry) in files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            Entries.Add(new FileEntryViewModel(
                new FileSystemEntry
                {
                    Name = name, FullPath = entry.Name, Kind = FileSystemEntryKind.File,
                    Size = entry.Size >= 0 ? entry.Size : null, Modified = entry.Modified,
                },
                display: _display));

        CurrentPath = _archiveInternal.Length == 0 ? _archivePath! : $"{_archivePath}/{_archiveInternal}";
        StatusText = $"{dirs.Count + files.Count} items in {Path.GetFileName(_archivePath!)}"
            + (_archiveInternal.Length > 0 ? $"/{_archiveInternal}" : string.Empty);
        NotifyNavigationState();
        Navigated?.Invoke(this, EventArgs.Empty);
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

    /// <summary>Clicking a column header sorts by it (when the column maps to a sort key) — PRD §6.1.</summary>
    [RelayCommand]
    private void SortByColumn(ColumnSpec? column)
    {
        if (column?.Sort is { } sort)
            SortBy(sort);
    }

    [RelayCommand]
    private Task NavigatePathBarAsync()
    {
        IsEditingPath = false;
        return string.IsNullOrWhiteSpace(PathBarText) ? Task.CompletedTask : NavigateToAsync(PathBarText.Trim());
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    /// <summary>Navigates <em>this</em> pane to a sidebar item (favorite/drive/partition/device).</summary>
    [RelayCommand]
    private Task OpenSidebarItem(SidebarItemViewModel? item) =>
        item is { IsNavigable: true } ? NavigateToAsync(item.Path) : Task.CompletedTask;

    /// <summary>Switches the combined path bar into editable mode, pre-filled with the current path.</summary>
    [RelayCommand]
    private void BeginEditPath()
    {
        PathBarText = CurrentPath;
        IsEditingPath = true;
    }

    /// <summary>Leaves editable mode without navigating, restoring the breadcrumb view.</summary>
    [RelayCommand]
    private void CancelEditPath()
    {
        IsEditingPath = false;
        PathBarText = CurrentPath;
    }

    [RelayCommand]
    private Task NavigateBreadcrumbAsync(BreadcrumbSegment? segment)
    {
        if (segment is null)
            return Task.CompletedTask;

        switch (segment.Kind)
        {
            // A real folder is a real navigation even from inside an archive — that is how you get
            // back out of one by clicking, which the trail could not express before.
            case BreadcrumbKind.Folder:
                return NavigateToAsync(segment.Path);

            case BreadcrumbKind.Archive:
                _archiveInternal = string.Empty;
                ShowArchiveDir();
                return Task.CompletedTask;

            default:
                // The segment carries the whole path; the archive only wants the part inside it.
                _archiveInternal = _archivePath is not null && segment.Path.Length > _archivePath.Length
                    ? segment.Path[(_archivePath.Length + 1)..]
                    : string.Empty;
                ShowArchiveDir();
                return Task.CompletedTask;
        }
    }

    /// <summary>Rebuilds the breadcrumb trail by walking parents from the current folder to the root.</summary>
    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();

        // Inside an archive the trail is the real path down to the archive file, then the
        // directories within it — one continuous path, because that is what it is.
        var trail = _archivePath is not null
            ? FilesystemTrail(_archivePath)
            : string.IsNullOrEmpty(CurrentPath) ? [] : FilesystemTrail(CurrentPath);

        if (_archivePath is not null)
        {
            if (trail.Count > 0)
                trail[^1] = trail[^1] with { Kind = BreadcrumbKind.Archive };

            if (_archiveInternal.Length > 0)
            {
                var inside = _archivePath;
                foreach (var part in _archiveInternal.Split('/'))
                {
                    inside = $"{inside}/{part}";
                    trail.Add(new BreadcrumbSegment(part, inside, Kind: BreadcrumbKind.ArchiveEntry));
                }
            }
        }

        for (var i = 0; i < trail.Count; i++)
            Breadcrumbs.Add(trail[i] with { ShowSeparator = i > 0 });
    }

    /// <summary>The trail of real directories from the filesystem root down to <paramref name="path"/>.</summary>
    private List<BreadcrumbSegment> FilesystemTrail(string path)
    {
        var trail = new List<BreadcrumbSegment>();
        var cursor = path;
        var guard = 0;
        while (!string.IsNullOrEmpty(cursor) && guard++ < 256)
        {
            var name = Path.GetFileName(cursor.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            trail.Insert(0, new BreadcrumbSegment(string.IsNullOrEmpty(name) ? cursor : name, cursor));

            var parent = _fileSystem.GetParent(cursor);
            if (parent is null || string.Equals(parent, cursor, StringComparison.Ordinal))
                break;
            cursor = parent;
        }

        return trail;
    }

    [RelayCommand]
    private async Task StartSearchAsync()
    {
        // Allow searching by extension alone (empty query = match every name, then filter by extension).
        if (string.IsNullOrEmpty(CurrentPath)
            || (string.IsNullOrWhiteSpace(SearchQuery) && string.IsNullOrWhiteSpace(SearchExtensions)))
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
                Entries.Add(NewEntry(hit, Path.GetDirectoryName(hit.FullPath)));
                count++;
                if ((count & 31) == 0)
                    StatusText = $"Searching… {count} matches";
            }
            var label = string.IsNullOrWhiteSpace(SearchQuery) ? SearchExtensions.Trim() : SearchQuery.Trim();
            StatusText = $"{count} matches for “{label}”";
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
        // A fresh directory load ends any in-progress search and leaves archive-browsing mode.
        _searchCts?.Cancel();
        IsSearching = false;
        _archivePath = null;

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

            // Only a folder that actually opened is worth remembering — a path that failed to load
            // is precisely the one nobody wants offered back to them.
            FolderOpened?.Invoke(this, path);

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
        // Inside an archive the listing comes from the archive index, not _rawEntries.
        if (_archivePath is not null)
        {
            ShowArchiveDir();
            return;
        }

        IEnumerable<FileSystemEntry> visible = ShowHidden ? _rawEntries : _rawEntries.Where(e => !e.IsHidden);
        if (!string.IsNullOrWhiteSpace(FilterText))
            visible = visible.Where(e => FuzzyMatcher.IsMatch(FilterText, e.Name));
        if (!string.IsNullOrEmpty(TagFilter))
            visible = visible.Where(e => string.Equals(TagLookup?.Invoke(e.FullPath), TagFilter, StringComparison.OrdinalIgnoreCase));
        var sorted = EntrySorter.Sort(visible, SortColumn, SortDirection);

        Entries.Clear();
        foreach (var entry in sorted)
            Entries.Add(NewEntry(entry));

        var folders = sorted.Count(e => e.IsDirectory);
        StatusText = $"{sorted.Count} items ({folders} folders, {sorted.Count - folders} files)";

        ScheduleFolderSizes();
    }

    // ---- background folder sizing (PRD §6.2) ----

    /// <summary>
    /// Kicks off recursive size calculation for every folder currently shown. Cached sizes are
    /// applied instantly; the rest compute in the background (throttled by the size service) and
    /// fill in live. Rescheduling cancels any still-running walks from the previous view.
    /// </summary>
    private void ScheduleFolderSizes()
    {
        _sizeCts?.Cancel();
        _sizeCts?.Dispose();
        if (_archivePath is not null)
            return; // virtual archive folders have no on-disk size to compute
        var cts = _sizeCts = new CancellationTokenSource();
        var token = cts.Token;

        foreach (var vm in Entries)
        {
            if (!vm.IsDirectory || vm.Entry.Kind == FileSystemEntryKind.Drive)
                continue;

            if (_sizes.TryGetCached(vm.FullPath, out var cached))
            {
                vm.ComputedSize = cached;
                continue;
            }

            vm.IsCalculatingSize = true;
            _ = CalculateSizeAsync(vm, token);
        }
    }

    private async Task CalculateSizeAsync(FileEntryViewModel vm, CancellationToken token)
    {
        try
        {
            // Progress<T> marshals to the captured (UI) context, so the running total updates safely.
            var progress = new Progress<long>(running => vm.ComputedSize = running);
            var size = await _sizes.GetSizeAsync(vm.FullPath, progress, token);
            Post(() =>
            {
                vm.ComputedSize = size;
                vm.IsCalculatingSize = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Navigated away or re-sorted; the new schedule owns the folders now.
        }
    }

    private void Post(Action action)
    {
        if (_sync is not null)
            _sync.Post(_ => action(), null);
        else
            action();
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
        _sizeCts?.Cancel();
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

    partial void OnShowHiddenChanged(bool value) => RebuildEntries();
    partial void OnFilterTextChanged(string value) => RebuildEntries();
    partial void OnTagFilterChanged(string? value) => RebuildEntries();
    partial void OnCurrentPathChanged(string value)
    {
        PathBarText = value;
        RebuildBreadcrumbs();
        UpdateTitle();
    }

    // Clear any active filter when navigating so a new folder shows in full.
    partial void OnCurrentPathChanging(string value)
    {
        if (!string.IsNullOrEmpty(FilterText))
            FilterText = string.Empty;
    }
}
