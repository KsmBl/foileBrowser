using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoileBrowser.Docking;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// The window shell: a dockable set of panes, a sidebar, and a background operation queue, plus the
/// cross-pane file-operation commands (PRD §6.2, §6.3). Panes/tabs are held by the in-house
/// <see cref="DockLayout"/>, so any number can be opened and arranged by splitting or tabbing them.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystem;
    private readonly IFileOperationService _operations;
    private readonly ITrashService _trash;
    private readonly IPreviewService _previewService;
    private readonly ISettingsService _settings;
    private readonly ITagService _tags;
    private readonly IShellService _shell;
    private readonly IApplicationService _apps;
    private readonly IArchiveService _archives;
    private readonly IDeviceService _device;
    private readonly IDiskService _disk;
    private readonly IDirectorySizeService _sizes;
    private readonly IMetadataService _metadata;
    private readonly DisplayOptions _display = new();

    private CancellationTokenSource? _previewCts;
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    private Timer? _devicePoll;
    private string _volumeSignature = string.Empty;

    // The docking layout owns the panes and their tabs; the view (DockLayoutView) renders it and drives
    // its drag interactions (PRD §6.2).
    private FileTabViewModel? _lastOtherTab;

    [ObservableProperty]
    private DockLayout _layout = null!;

    [ObservableProperty]
    private FileTabViewModel? _activeTab;

    /// <summary>Every open tab across all panes, left-to-right.</summary>
    private IEnumerable<FileTabViewModel> AllTabs => Layout.Panes().SelectMany(p => p.Tabs).OfType<FileTabViewModel>();

    /// <summary>Every open tab across all panes (snapshot).</summary>
    public IReadOnlyList<FileTabViewModel> Tabs => AllTabs.ToList();

    /// <summary>True when a tab exists in a different pane than the active one (enables copy/move).</summary>
    public bool IsDualPane => OtherTab is not null;

    private DockPane? PaneOf(FileTabViewModel tab) => Layout.PaneOf(tab);
    private bool SamePane(FileTabViewModel a, FileTabViewModel b) => ReferenceEquals(PaneOf(a), PaneOf(b));

    public OperationQueueViewModel OperationQueue { get; }
    public CommandPaletteViewModel CommandPalette { get; }

    /// <summary>Navigation-sidebar sections in display order (drag-reorderable — PRD §6.2).</summary>
    public ObservableCollection<SidebarSectionViewModel> Sections { get; } = [];

    /// <summary>The global operations-toolbar buttons, in display order (drag-reorderable — PRD §6.8).</summary>
    public ObservableCollection<ToolbarItemViewModel> ToolbarItems { get; } = [];

    /// <summary>Visible file-list columns, in order (shared by every pane's header + rows so they align,
    /// and resizable/reorderable/toggleable — PRD §6.1).</summary>
    public ObservableCollection<ColumnSpec> Columns { get; } = [];

    /// <summary>Every column the user can show/hide (built-ins + registered metadata columns).</summary>
    public IReadOnlyList<ColumnSpec> AvailableColumns => ColumnCatalog.All;

    /// <summary>Root nodes of the sidebar folder-tree navigator (PRD §6.2), built lazily when enabled.</summary>
    public ObservableCollection<FolderNodeViewModel> TreeRoots { get; } = [];

    /// <summary>Whether the folder-tree section is shown in each pane's sidebar (Settings ▸ Sidebar).</summary>
    [ObservableProperty]
    private bool _isTreeVisible;

    // Inspector panel state (PRD §6.5).
    [ObservableProperty]
    private PreviewResult? _preview;

    [ObservableProperty]
    private bool _isInspectorOpen = true;

    /// <summary>Whether the emoji operations toolbar is shown (View ▸ Toolbar, persisted).</summary>
    [ObservableProperty]
    private bool _isToolbarVisible = true;

    /// <summary>Whether every pane's filter/search bar is shown; when off it's revealed on demand by
    /// Ctrl+F for the session and collapsed again on Escape (PRD §6.4).</summary>
    [ObservableProperty]
    private bool _isSearchBarVisible = true;

    /// <summary>Escape from a revealed-on-demand search bar returns it to its configured state.</summary>
    public void CollapseSearchBar() => IsSearchBarVisible = _settings.Current.SearchBarVisible;

    /// <summary>Set by the view to prompt the user for a name (rename). Returns null if cancelled.</summary>
    public Func<string, Task<string?>>? NameRequester { get; set; }

    /// <summary>Set by the view to show the properties window for an entry (Alt+Enter — PRD §6.1).</summary>
    public Func<FileSystemEntry, Task>? PropertiesRequester { get; set; }

    /// <summary>The background size service, exposed so the properties window can compute a folder's size.</summary>
    public IDirectorySizeService Sizes => _sizes;

    /// <summary>Application associations, used by the Properties window's default-app picker (PRD §6.9).</summary>
    public IApplicationService Applications => _apps;

    /// <summary>Raised when the VM wants text placed on the clipboard (path/name copy — PRD §6.3).</summary>
    public event EventHandler<string>? ClipboardCopyRequested;

    /// <summary>Design-time constructor for the XAML previewer only.</summary>
    public MainWindowViewModel()
        : this(new FileSystemService(), new FileOperationService(), new TrashService())
    {
    }

    public MainWindowViewModel(
        IFileSystemService fileSystem, IFileOperationService operations, ITrashService trash,
        ISearchService? search = null, IPreviewService? preview = null,
        ISettingsService? settings = null, ITagService? tags = null, IShellService? shell = null,
        IArchiveService? archives = null, IDeviceService? device = null, IDiskService? disk = null,
        IApplicationService? apps = null)
    {
        _apps = apps ?? new ApplicationService();
        _fileSystem = fileSystem;
        _operations = operations;
        _trash = trash;
        _previewService = preview ?? new PreviewService();
        _settings = settings ?? new SettingsService();
        _tags = tags ?? new TagService(_settings);
        _shell = shell ?? new ShellService();
        _archives = archives ?? new ArchiveService();
        _device = device ?? new DeviceService();
        _disk = disk ?? new DiskService();
        _sizes = new DirectorySizeService();
        _metadata = new MetadataService();
        _search = search ??= new SearchService();

        // Add the metadata columns (image/audio/video) to the catalogue so they're offered in the
        // header's add/remove menu (PRD §6.1).
        ColumnCatalog.Register(_metadata.Columns.Select(c => new ColumnSpec
        {
            Id = c.Id,
            Header = c.Header,
            Kind = c.Category == "Media" ? ColumnKind.Video : ColumnKind.Image,
            RightAligned = c.RightAligned,
            DefaultWidth = c.Width,
            Width = c.Width,
        }));

        var first = CreateTab();
        var second = CreateTab();
        _layout = BuildLayoutFrom([([first], 0), ([second], 0)]);
        WireLayout(_layout);
        _activeTab = first;

        OperationQueue = new OperationQueueViewModel(operations);
        OperationQueue.OperationCompleted += (_, _) => RefreshPanes();

        _commands = BuildCommands().ToList();
        CommandPalette = new CommandPaletteViewModel(_commands);
    }

    private readonly ISearchService _search;

    // The command registry doubles as the source of truth for hotkeys and the palette (PRD §6.6).
    private readonly List<CommandItem> _commands;

    /// <summary>Every registered command (palette, menus, hotkeys).</summary>
    public IReadOnlyList<CommandItem> Commands => _commands;

    /// <summary>Window-wide commands the keybind editor can rebind (PRD §6.6).</summary>
    public IReadOnlyList<CommandItem> RebindableCommands => _commands.Where(c => c.Global).ToList();

    /// <summary>Raised after hotkeys are (re)loaded so the view rebuilds its window key bindings.</summary>
    public event EventHandler? KeybindsChanged;

    /// <summary>Applies persisted hotkey overrides onto the live commands, then asks the view to rebind.</summary>
    private void ApplyKeybinds()
    {
        foreach (var command in _commands)
            // An explicit override wins (empty string = deliberately unbound); no key = ship default.
            command.Gesture = _settings.Current.Keybinds.TryGetValue(command.Id, out var g)
                ? (string.IsNullOrWhiteSpace(g) ? null : g.Trim())
                : command.DefaultGesture;
        KeybindsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Creates a folder tab and subscribes to it (the docking layout owns placement).</summary>
    private FileTabViewModel CreateTab()
    {
        var tab = new FileTabViewModel(_fileSystem, _search, _shell, _archives, _sizes, _display, _metadata)
        {
            TagLookup = _tags.GetTag,
        };
        tab.PropertyChanged += OnTabPropertyChanged;
        return tab;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, ActiveTab))
            return;
        if (e.PropertyName == nameof(FileTabViewModel.SelectedEntry))
            _ = UpdatePreviewAsync();
        else if (e.PropertyName == nameof(FileTabViewModel.CurrentPath))
            ReRootTreeToCurrent(); // "Current folder" tree follows the active pane
    }

    /// <summary>Builds a layout from a list of panes (each a set of tabs + active index), tiled horizontally.</summary>
    private static DockLayout BuildLayoutFrom(List<(List<FileTabViewModel> Tabs, int Active)> panes)
    {
        DockNode root;
        if (panes.Count <= 1)
        {
            root = MakePane(panes.Count == 1 ? panes[0] : ([], 0));
        }
        else
        {
            var split = new DockSplit { Orientation = DockOrientation.Horizontal };
            foreach (var pane in panes.Select(MakePane))
            {
                pane.Parent = split;
                split.Children.Add(pane);
            }
            root = split;
        }
        return new DockLayout(root);
    }

    private static DockPane MakePane((List<FileTabViewModel> Tabs, int Active) spec)
    {
        var pane = new DockPane();
        foreach (var tab in spec.Tabs)
            pane.Tabs.Add(tab);
        pane.ActiveTab = spec.Tabs.Count > 0 ? spec.Tabs[Math.Clamp(spec.Active, 0, spec.Tabs.Count - 1)] : null;
        return pane;
    }

    /// <summary>Subscribes to a layout's active/close events (rewired when the layout is replaced).</summary>
    private void WireLayout(DockLayout layout)
    {
        layout.TabClosed += OnLayoutTabClosed;
        layout.PropertyChanged += OnLayoutPropertyChanged;
    }

    partial void OnLayoutChanged(DockLayout? oldValue, DockLayout newValue)
    {
        if (oldValue is not null)
        {
            oldValue.TabClosed -= OnLayoutTabClosed;
            oldValue.PropertyChanged -= OnLayoutPropertyChanged;
        }
        WireLayout(newValue);
    }

    private void OnLayoutPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockLayout.ActiveDockable))
            SetActiveTab(Layout.ActiveDockable as FileTabViewModel);
    }

    private void OnLayoutTabClosed(object? sender, IDockable tab)
    {
        if (tab is not FileTabViewModel closed)
            return;
        closed.PropertyChanged -= OnTabPropertyChanged;
        closed.Dispose();
        if (ReferenceEquals(_lastOtherTab, closed))
            _lastOtherTab = null;
        NotifyPaneCountChanged();
    }

    /// <summary>Ctrl+T: opens a new tab in the active pane.</summary>
    [RelayCommand]
    private async Task AddTab()
    {
        var tab = CreateTab();
        Layout.AddTab(tab, Layout.ActivePane);
        await tab.InitializeAsync();
    }

    /// <summary>New Pane: opens a new tab split into its own pane to the right, and focuses it.</summary>
    [RelayCommand]
    private async Task AddPane()
    {
        var tab = CreateTab();
        var target = Layout.ActivePane ?? Layout.Panes().FirstOrDefault();
        if (target is not null)
            Layout.Split(tab, target, DockSide.Right);
        else
            Layout.AddTab(tab);
        await tab.InitializeAsync();
    }

    /// <summary>Called by a tab's view when it gains focus, so operations target it.</summary>
    public void ActivateTab(FileTabViewModel tab)
    {
        if (!ReferenceEquals(ActiveTab, tab))
            Layout.Activate(tab);
    }

    private void SetActiveTab(FileTabViewModel? tab)
    {
        // Remember the previously-active tab (if in another pane) as the F6/F7 transfer target.
        if (ActiveTab is { } prev && tab is not null && AllTabs.Contains(prev) && !SamePane(prev, tab))
            _lastOtherTab = prev;
        ActiveTab = tab;
    }

    partial void OnActiveTabChanged(FileTabViewModel? oldValue, FileTabViewModel? newValue)
    {
        NotifyPaneCountChanged();
        RewireInspector();
        ReRootTreeToCurrent(); // switching panes re-roots a "Current folder" tree to that pane
    }

    /// <summary>Transfer target for F6/F7: the most-recently-active tab in a different pane.</summary>
    private FileTabViewModel? OtherTab
    {
        get
        {
            if (ActiveTab is not { } a)
                return null;
            var pane = PaneOf(a);
            if (_lastOtherTab is { } p && AllTabs.Contains(p) && !ReferenceEquals(PaneOf(p), pane))
                return p;
            return AllTabs.FirstOrDefault(t => !ReferenceEquals(t, a) && !ReferenceEquals(PaneOf(t), pane));
        }
    }

    private void NotifyPaneCountChanged()
    {
        OnPropertyChanged(nameof(IsDualPane));
        CopyToOtherCommand.NotifyCanExecuteChanged();
        MoveToOtherCommand.NotifyCanExecuteChanged();
    }

    public AppSettings Settings => _settings.Current;

    public IReadOnlyList<TagColor> TagPalette => _tags.Palette;

    /// <summary>
    /// Applications offered by the selection's "Open with" submenu (PRD §6.9). Refilled by
    /// <see cref="RefreshOpenWithAsync"/> when the context menu opens, so the .desktop scan only
    /// happens on demand rather than on every selection change.
    /// </summary>
    public ObservableCollection<DesktopApp> OpenWithApps { get; } = [];

    /// <summary>Whether the "Open with" submenu has anything to show for the current selection.</summary>
    [ObservableProperty]
    private bool _hasOpenWithApps;

    /// <summary>Repopulates <see cref="OpenWithApps"/> for the current selection.</summary>
    public async Task RefreshOpenWithAsync()
    {
        OpenWithApps.Clear();
        HasOpenWithApps = false;
        if (ActiveTab?.SelectedEntry is not { IsDirectory: false } entry)
            return;

        foreach (var app in await _apps.GetCandidatesAsync(entry.FullPath))
            OpenWithApps.Add(app);
        HasOpenWithApps = OpenWithApps.Count > 0;
    }

    /// <summary>Opens the selection with a specific application from the "Open with" submenu.</summary>
    [RelayCommand]
    private Task OpenWithApp(DesktopApp? app) =>
        app is not null && ActiveTab?.SelectedEntry is { } e
            ? _apps.LaunchAsync(app, e.FullPath)
            : Task.CompletedTask;

    [RelayCommand]
    private void OpenCommandPalette() => CommandPalette.Open();

    /// <summary>Registers every palette-visible action (PRD §6.6). Tab-scoped commands resolve the active tab lazily.</summary>
    private IEnumerable<CommandItem> BuildCommands()
    {
        Task Tab(Func<FileTabViewModel, Task> action) => ActiveTab is { } t ? action(t) : Task.CompletedTask;

        return
        [
            new("app.commandPalette", "Command Palette", "App", "Ctrl+P", () => { OpenCommandPaletteCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("nav.editPath", "Edit Path", "Navigate", "Ctrl+L", () => { FocusPathBarCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("search.focus", "Find in Folder", "Search", "Ctrl+F", () => { FocusSearchCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("file.newFolder", "New Folder", "File", "Ctrl+Shift+N", () => NewFolderCommand.ExecuteAsync(null), global: true),
            new("file.newFile", "New File", "File", null, () => NewFileCommand.ExecuteAsync(null), global: true),
            new("file.rename", "Rename…", "File", "F2", () => RenameSelectedCommand.ExecuteAsync(null)),
            new("file.delete", "Delete to Trash", "File", "Delete", () => DeleteSelectedCommand.ExecuteAsync(null)),
            new("file.properties", "Properties", "File", "Alt+Enter", () => ShowPropertiesCommand.ExecuteAsync(null), global: true),
            new("file.copyToOther", "Copy to Other Pane", "File", "F6", () => { CopyToOtherCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("file.moveToOther", "Move to Other Pane", "File", "F7", () => { MoveToOtherCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("file.copyPath", "Copy Path", "File", null, () => { CopyPathCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("file.copyName", "Copy Name", "File", null, () => { CopyNameCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("nav.back", "Go Back", "Navigate", "Alt+Left", () => Tab(t => t.GoBackCommand.ExecuteAsync(null)), global: true),
            new("nav.forward", "Go Forward", "Navigate", "Alt+Right", () => Tab(t => t.GoForwardCommand.ExecuteAsync(null)), global: true),
            new("nav.up", "Go Up", "Navigate", "Alt+Up", () => Tab(t => t.GoUpCommand.ExecuteAsync(null)), global: true),
            new("nav.refresh", "Refresh", "Navigate", "F5", () => Tab(t => t.RefreshCommand.ExecuteAsync(null)), global: true),
            new("view.newPane", "New Pane (split)", "View", null, () => AddPaneCommand.ExecuteAsync(null), global: true),
            new("view.toggleInspector", "Toggle Inspector", "View", "Ctrl+I", () => { ToggleInspectorCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("view.toggleToolbar", "Toggle Toolbar", "View", null, () => { ToggleToolbarCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("view.toggleHidden", "Toggle Hidden Files", "View", null, () => Tab(t => { t.ShowHidden = !t.ShowHidden; return Task.CompletedTask; }), global: true),
            new("view.sizeUnit", "Cycle Size Units (KiB/KB/Bytes)", "View", null, () => CycleSizeUnitCommand.ExecuteAsync(null), global: true),
            new("view.dateFormat", "Cycle Date Format (absolute/relative)", "View", null, () => CycleDateFormatCommand.ExecuteAsync(null), global: true),
            new("tab.new", "New Tab", "Tab", "Ctrl+T", () => AddTabCommand.ExecuteAsync(null), global: true),
            new("tab.close", "Close Tab", "Tab", "Ctrl+W", () => { CloseTabCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("search.stop", "Stop Search", "Search", null, () => Tab(t => t.StopSearchCommand.ExecuteAsync(null)), global: true),
            new("file.batchRename", "Batch Rename…", "File", null, () => BatchRenameCommand.ExecuteAsync(null), global: true),
            new("os.terminal", "Open Terminal Here", "System", null, () => OpenTerminalHereCommand.ExecuteAsync(null), global: true),
            new("os.openWith", "Open with Default App", "System", null, () => OpenSelectedExternallyCommand.ExecuteAsync(null), global: true),
            new("fav.pin", "Pin Current Folder", "Favorites", null, () => PinFavoriteCommand.ExecuteAsync(null), global: true),
            new("tag.clear", "Clear Tag", "Tag", null, () => ClearTagCommand.ExecuteAsync(null), global: true),
            new("tag.filterClear", "Clear Tag Filter", "Tag", null, () => { ClearTagFilterCommand.Execute(null); return Task.CompletedTask; }, global: true),
            new("app.settings", "Settings…", "App", null, () => OpenSettingsCommand.ExecuteAsync(null), global: true),
            new("archive.extract", "Extract Archive Here", "Archive", null, () => ExtractHereCommand.ExecuteAsync(null), global: true),
            new("archive.identify", "Identify File", "Archive", null, () => { IdentifyFileCommand.Execute(null); return Task.CompletedTask; }, global: true),
            .. _tags.Palette.Select(c => new CommandItem(
                $"tag.set.{c.Name}", $"Tag: {c.Name}", "Tag", null, () => AssignTagCommand.ExecuteAsync(c.Hex))),
            .. _tags.Palette.Select(c => new CommandItem(
                $"tag.filter.{c.Name}", $"Filter by Tag: {c.Name}", "Tag", null, () => { FilterByTagCommand.Execute(c.Hex); return Task.CompletedTask; })),
        ];
    }

    /// <summary>Closes the active tab (Ctrl+W); keeps at least one tab open overall.</summary>
    [RelayCommand]
    private void CloseTab()
    {
        if (ActiveTab is { } tab && AllTabs.Count() > 1)
            Layout.CloseTab(tab);
    }

    public async Task InitializeAsync()
    {
        await _settings.LoadAsync();
        IsInspectorOpen = _settings.Current.IsInspectorOpen;
        IsToolbarVisible = _settings.Current.IsToolbarVisible;
        IsSearchBarVisible = _settings.Current.SearchBarVisible;
        _shell.TerminalCommand = _settings.Current.TerminalCommand;

        if (Enum.TryParse<SizeUnit>(_settings.Current.SizeUnit, out var unit))
            _display.SizeUnit = unit;
        if (Enum.TryParse<DateDisplay>(_settings.Current.DateFormat, out var date))
            _display.DateDisplay = date;
        BuildToolbar();
        BuildColumns();
        UpdateDisplayLabels();

        await RestoreTabsAsync(_settings.Current.Session);

        await LoadSidebarAsync();
        RewireInspector();
        StartDevicePolling();
        ApplyKeybinds();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rebuilds the panes and their tabs from the saved session (PRD §6.2).</summary>
    private async Task RestoreTabsAsync(SessionLayout session)
    {
        // Discard the placeholder tabs the constructor created.
        foreach (var placeholder in AllTabs.ToList())
        {
            placeholder.PropertyChanged -= OnTabPropertyChanged;
            placeholder.Dispose();
        }

        // Prefer the full tree (nested splits); fall back to the flat pane list for old sessions.
        DockLayout? restored = session.Tree is { } tree ? DockNodeState.Restore(tree, RestoreTab) : null;

        if (restored is null)
        {
            var paneSpecs = session.Panes.Count > 0
                ? session.Panes
                :
                [
                    new PaneSession { Tabs = session.LeftTabs, ActiveIndex = session.LeftActiveIndex },
                    new PaneSession { Tabs = session.RightTabs, ActiveIndex = session.RightActiveIndex },
                ];

            var panes = new List<(List<FileTabViewModel> Tabs, int Active)>();
            foreach (var spec in paneSpecs)
            {
                var valid = spec.Tabs.Where(p => !string.IsNullOrEmpty(p) && _fileSystem.DirectoryExists(p)).ToList();
                if (valid.Count == 0)
                {
                    // An empty pane spec (or first run's two empty panes) gets one default home tab.
                    var tab = CreateTab();
                    await tab.InitializeAsync();
                    panes.Add(([tab], 0));
                }
                else
                {
                    var tabs = valid.Select(p => { var t = CreateTab(); _ = t.NavigateToAsync(p); return t; }).ToList();
                    panes.Add((tabs, spec.ActiveIndex));
                }
            }
            if (panes.Count > 0)
                restored = BuildLayoutFrom(panes);
        }

        if (restored is null)
        {
            var tab = CreateTab();
            await tab.InitializeAsync();
            restored = BuildLayoutFrom([([tab], 0)]);
        }

        Layout = restored;
        ActiveTab = restored.ActiveDockable as FileTabViewModel ?? AllTabs.FirstOrDefault();
        if (ActiveTab is not null)
            restored.Activate(ActiveTab);
        NotifyPaneCountChanged();
    }

    /// <summary>Factory used by tree restore: a tab for a saved folder path, or null to skip an invalid one.</summary>
    private IDockable? RestoreTab(string path)
    {
        if (string.IsNullOrEmpty(path) || !_fileSystem.DirectoryExists(path))
            return null;
        var tab = CreateTab();
        _ = tab.NavigateToAsync(path); // populates asynchronously
        return tab;
    }

    /// <summary>Snapshots the layout tree (and a flat mirror) into settings and persists (on window close).</summary>
    public Task SaveSessionAsync()
    {
        var tree = DockNodeState.Capture(Layout.Root, t =>
            t is FileTabViewModel { CurrentPath: { Length: > 0 } p } ? p : null);

        var paneSessions = Layout.Panes()
            .Select(pane => new PaneSession
            {
                Tabs = pane.Tabs.OfType<FileTabViewModel>().Select(t => t.CurrentPath).Where(p => !string.IsNullOrEmpty(p)).ToList(),
                ActiveIndex = pane.ActiveTab is FileTabViewModel a ? Math.Max(0, pane.Tabs.IndexOf(a)) : 0,
            })
            .Where(s => s.Tabs.Count > 0)
            .ToList();

        var session = new SessionLayout { Tree = tree, Panes = paneSessions };
        // Mirror the first two panes into the legacy fields so downgrades still restore something.
        if (paneSessions.Count > 0)
            (session.LeftTabs, session.LeftActiveIndex) = (paneSessions[0].Tabs, paneSessions[0].ActiveIndex);
        if (paneSessions.Count > 1)
            (session.RightTabs, session.RightActiveIndex) = (paneSessions[1].Tabs, paneSessions[1].ActiveIndex);

        _settings.Current.Session = session;
        _settings.Current.IsDualPane = IsDualPane;
        _settings.Current.IsInspectorOpen = IsInspectorOpen;
        _settings.Current.IsToolbarVisible = IsToolbarVisible;
        return _settings.SaveAsync();
    }

    // ---- inspector / preview ----

    [RelayCommand]
    private void ToggleInspector() => IsInspectorOpen = !IsInspectorOpen;

    [RelayCommand]
    private void ToggleToolbar() => IsToolbarVisible = !IsToolbarVisible;

    // ---- size / date display modes (PRD §6.1, §6.2) ----

    /// <summary>Toolbar label showing the current size unit (also the cycle button's caption).</summary>
    [ObservableProperty]
    private string _sizeUnitLabel = "KiB";

    /// <summary>Toolbar label showing the current date format.</summary>
    [ObservableProperty]
    private string _dateFormatLabel = "Date";

    [RelayCommand]
    private Task CycleSizeUnit()
    {
        _display.SizeUnit = _display.SizeUnit switch
        {
            SizeUnit.Binary => SizeUnit.Decimal,
            SizeUnit.Decimal => SizeUnit.Bytes,
            _ => SizeUnit.Binary,
        };
        return ApplyDisplayChangeAsync();
    }

    [RelayCommand]
    private Task CycleDateFormat()
    {
        _display.DateDisplay = _display.DateDisplay == DateDisplay.Absolute ? DateDisplay.Relative : DateDisplay.Absolute;
        return ApplyDisplayChangeAsync();
    }

    private async Task ApplyDisplayChangeAsync()
    {
        UpdateDisplayLabels();
        RefreshAllDisplays();
        _settings.Current.SizeUnit = _display.SizeUnit.ToString();
        _settings.Current.DateFormat = _display.DateDisplay.ToString();
        await _settings.SaveAsync();
    }

    private void UpdateDisplayLabels()
    {
        SizeUnitLabel = _display.SizeUnit switch
        {
            SizeUnit.Binary => "KiB",
            SizeUnit.Decimal => "KB",
            _ => "Bytes",
        };
        DateFormatLabel = _display.DateDisplay == DateDisplay.Relative ? "Ago" : "Date";

        // The size/date toolbar buttons show these live labels.
        foreach (var item in ToolbarItems)
        {
            if (item.Id == "sizeUnit") item.Content = SizeUnitLabel;
            else if (item.Id == "dateFormat") item.Content = DateFormatLabel;
        }
    }

    // ---- global operations toolbar (data-driven so it can be reordered — PRD §6.8) ----

    /// <summary>(Re)builds the toolbar buttons in the persisted order, marking hidden ones invisible.</summary>
    private void BuildToolbar()
    {
        (string Id, string Content, string Tip, ICommand Cmd, double Fs)[] defs =
        [
            ("newFolder", "📁", "New folder (Ctrl+Shift+N)", NewFolderCommand, 15),
            ("newFile", "📄", "New file", NewFileCommand, 15),
            ("rename", "✏️", "Rename (F2)", RenameSelectedCommand, 15),
            ("delete", "🗑️", "Delete to trash (Del)", DeleteSelectedCommand, 15),
            ("copyToOther", "📋➡️", "Copy to other pane (F6)", CopyToOtherCommand, 15),
            ("moveToOther", "✂️➡️", "Move to other pane (F7)", MoveToOtherCommand, 15),
            ("copyPath", "🔗", "Copy path", CopyPathCommand, 15),
            ("copyName", "🏷️", "Copy name", CopyNameCommand, 15),
            ("batchRename", "🔤", "Batch rename items in this folder", BatchRenameCommand, 15),
            ("terminal", "💻", "Open terminal here", OpenTerminalHereCommand, 15),
            ("pin", "⭐", "Pin current folder to favorites", PinFavoriteCommand, 15),
            ("newTab", "🗂️", "New tab (Ctrl+T) — drag a tab out to make a new pane", AddTabCommand, 15),
            ("inspector", "🔍", "Toggle inspector (Ctrl+I)", ToggleInspectorCommand, 15),
            ("sizeUnit", SizeUnitLabel, "Size units: binary (KiB) → decimal (KB) → bytes", CycleSizeUnitCommand, 12),
            ("dateFormat", DateFormatLabel, "Date format: absolute ↔ relative (e.g. 5 min ago)", CycleDateFormatCommand, 12),
            ("settings", "⚙️", "Settings", OpenSettingsCommand, 15),
        ];

        var byId = defs.ToDictionary(d => d.Id);
        var order = _settings.Current.ToolbarOrder;
        // Saved order first (ignoring unknown/removed ids), then any new buttons appended in default order.
        var ids = order.Where(byId.ContainsKey)
            .Concat(defs.Select(d => d.Id).Where(id => !order.Contains(id)));

        var hidden = _settings.Current.HiddenToolbarButtons;
        ToolbarItems.Clear();
        foreach (var id in ids)
        {
            var d = byId[id];
            ToolbarItems.Add(new ToolbarItemViewModel
            {
                Id = d.Id,
                Content = d.Content,
                Tooltip = d.Tip,
                Command = d.Cmd,
                FontSize = d.Fs,
                IsVisible = !hidden.Contains(d.Id),
            });
        }
    }

    /// <summary>Drops the dragged toolbar button just before <paramref name="toId"/> and persists the order.</summary>
    public void MoveToolbarItem(string fromId, string toId)
    {
        if (fromId == toId)
            return;
        var from = ToolbarItems.FirstOrDefault(i => i.Id == fromId);
        var to = ToolbarItems.FirstOrDefault(i => i.Id == toId);
        if (from is null || to is null)
            return;

        ToolbarItems.Remove(from);
        ToolbarItems.Insert(ToolbarItems.IndexOf(to), from);
        _settings.Current.ToolbarOrder = ToolbarItems.Select(i => i.Id).ToList();
        _ = _settings.SaveAsync();
    }

    // ---- file-list columns (data-driven, resizable/reorderable/toggleable — PRD §6.1) ----

    /// <summary>(Re)builds the visible columns from settings (or defaults on a fresh profile).</summary>
    private void BuildColumns()
    {
        Columns.Clear();
        var saved = _settings.Current.Columns;
        var ids = saved.Count > 0
            ? saved.Select(c => (c.Id, c.Width))
            : ColumnCatalog.DefaultVisible.Select(id => (id, 0.0));

        foreach (var (id, width) in ids)
        {
            if (ColumnCatalog.Create(id) is not { } column)
                continue;
            if (width > 0)
                column.Width = width;
            Columns.Add(column);
        }
        if (Columns.Count == 0)
            foreach (var id in ColumnCatalog.DefaultVisible)
                if (ColumnCatalog.Create(id) is { } column)
                    Columns.Add(column);
    }

    /// <summary>Header right-click: shows or hides a column (keeps at least one visible).</summary>
    public void ToggleColumn(string id)
    {
        if (Columns.FirstOrDefault(c => c.Id == id) is { } existing)
        {
            if (Columns.Count > 1)
                Columns.Remove(existing);
        }
        else if (ColumnCatalog.Create(id) is { } column)
        {
            Columns.Add(column);
        }
        PersistColumns();
    }

    /// <summary>Drops the dragged column just before <paramref name="toId"/> and persists the order.</summary>
    public void MoveColumn(string fromId, string toId)
    {
        if (fromId == toId)
            return;
        var from = Columns.FirstOrDefault(c => c.Id == fromId);
        var to = Columns.FirstOrDefault(c => c.Id == toId);
        if (from is null || to is null)
            return;
        Columns.Remove(from);
        Columns.Insert(Columns.IndexOf(to), from);
        PersistColumns();
    }

    /// <summary>Persists the current column ids + widths (called after resize/reorder/toggle).</summary>
    public void PersistColumns()
    {
        _settings.Current.Columns = Columns.Select(c => new ColumnState { Id = c.Id, Width = c.Width }).ToList();
        _ = _settings.SaveAsync();
    }

    private void RefreshAllDisplays()
    {
        foreach (var tab in AllTabs)
            tab.RefreshDisplays();
    }

    // ---- menubar navigation wrappers (delegate to the active tab) ----

    [RelayCommand]
    private Task GoBack() => ActiveTab?.GoBackCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand]
    private Task GoForward() => ActiveTab?.GoForwardCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand]
    private Task GoUp() => ActiveTab?.GoUpCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand]
    private Task RefreshActive() => ActiveTab?.RefreshCommand.ExecuteAsync(null) ?? Task.CompletedTask;

    [RelayCommand]
    private void ToggleHidden()
    {
        if (ActiveTab is { } t)
            t.ShowHidden = !t.ShowHidden;
    }

    /// <summary>Ctrl+L: put the active tab's combined path bar into editable mode (Thunar/browser-style).</summary>
    [RelayCommand]
    private void FocusPathBar() => ActiveTab?.BeginEditPathCommand.Execute(null);

    /// <summary>Ctrl+F: reveal (if hidden) and focus the active tab's subtree-search box (PRD §6.4).</summary>
    [RelayCommand]
    private void FocusSearch()
    {
        IsSearchBarVisible = true; // a hidden bar is revealed for this session on demand
        ActiveTab?.FocusSearch();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        var version = typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "dev";
        if (ActiveTab is { } t)
            t.StatusText = $"foileBrowser {version} — a fast, keyboard-first, cross-platform file browser.";
    }

    /// <summary>Refreshes the inspector for the active tab's selection (each tab's changes are observed
    /// via <see cref="OnTabPropertyChanged"/>, so this just re-renders).</summary>
    private void RewireInspector() => _ = UpdatePreviewAsync();

    private async Task UpdatePreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        var cts = _previewCts = new CancellationTokenSource();

        var selected = ActiveTab?.SelectedEntry;
        if (selected is null || ActiveTab is null)
        {
            Preview = null;
            return;
        }

        try
        {
            // Inside an archive this streams the entry out to temp first, so previews work there too.
            var entry = await ActiveTab.ResolvePreviewEntryAsync(selected, cts.Token);
            if (entry is null)
            {
                if (!cts.Token.IsCancellationRequested)
                    Preview = null;
                return;
            }

            var result = await _previewService.CreateAsync(entry, cts.Token);
            if (!cts.Token.IsCancellationRequested)
                Preview = result;
        }
        catch (OperationCanceledException)
        {
            // A newer selection superseded this preview.
        }
    }

    // ---- sidebar ----

    private async Task LoadSidebarAsync()
    {
        var s = _settings.Current;
        var volumes = await _fileSystem.ListVolumesAsync();
        _volumeSignature = VolumeSignature(volumes);

        // Build each enabled section's content, keyed by id.
        var built = new Dictionary<string, SidebarSectionViewModel>();

        if (s.SidebarShowFavorites)
        {
            var section = new SidebarSectionViewModel { Id = "favorites", Title = "Favorites" };
            foreach (var fav in BuildFavorites())
                section.Items.Add(fav);
            built["favorites"] = section;
        }

        if (s.SidebarShowDrives)
        {
            var section = new SidebarSectionViewModel { Id = "drives", Title = "Drives" };
            AddVolumeGroups(section.Items, volumes.Where(v => v.Kind == VolumeKind.Fixed).ToList(), removableSection: false);
            built["drives"] = section;
        }

        var removable = volumes.Where(v => v.IsRemovable).ToList();
        if (s.SidebarShowDevices && removable.Count > 0)
        {
            var section = new SidebarSectionViewModel { Id = "devices", Title = "Devices" };
            AddVolumeGroups(section.Items, removable, removableSection: true);
            built["devices"] = section;
        }

        if (s.SidebarShowTree)
            built["tree"] = new SidebarSectionViewModel { Id = "tree", Title = "Folders", IsTree = true };

        // Order: the saved order first (custom drag order), then any not covered, in default order.
        Sections.Clear();
        foreach (var id in s.SidebarSectionOrder.Concat(DefaultSectionOrder).Distinct())
            if (built.TryGetValue(id, out var section))
                Sections.Add(section);

        // Folder tree: build its roots per the configured mode.
        IsTreeVisible = s.SidebarShowTree;
        if (s.SidebarShowTree)
            BuildTreeRoots(volumes);
        else
            TreeRoots.Clear();
    }

    private static readonly string[] DefaultSectionOrder = ["favorites", "drives", "devices", "tree"];

    /// <summary>Drops the dragged sidebar section just before <paramref name="toId"/> and persists the order.</summary>
    public void MoveSidebarSection(string fromId, string toId)
    {
        if (fromId == toId)
            return;
        var from = Sections.FirstOrDefault(x => x.Id == fromId);
        var to = Sections.FirstOrDefault(x => x.Id == toId);
        if (from is null || to is null)
            return;

        Sections.Remove(from);
        Sections.Insert(Sections.IndexOf(to), from);
        _settings.Current.SidebarSectionOrder = Sections.Select(x => x.Id).ToList();
        _ = _settings.SaveAsync();
    }

    private string _builtTreeMode = string.Empty;

    /// <summary>Builds the folder-tree roots for the configured root mode (PRD §6.2). Rebuilds when the
    /// mode changes but preserves expansion across device-poll refreshes within the same mode.</summary>
    private void BuildTreeRoots(IReadOnlyList<DriveVolume> volumes)
    {
        var mode = _settings.Current.TreeRoot;
        if (mode == "Current")
        {
            _builtTreeMode = mode;
            ReRootTreeToCurrent();
            return;
        }

        if (TreeRoots.Count > 0 && _builtTreeMode == mode)
            return;
        _builtTreeMode = mode;
        TreeRoots.Clear();

        if (mode == "Root")
        {
            if (OperatingSystem.IsWindows())
                foreach (var volume in volumes.Where(v => v.Kind == VolumeKind.Fixed))
                    TreeRoots.Add(new FolderNodeViewModel(volume.RootPath, volume.RootPath));
            else
                TreeRoots.Add(new FolderNodeViewModel("/", "/"));
            return;
        }

        // "HomeAndDrives"
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && _fileSystem.DirectoryExists(home))
            TreeRoots.Add(new FolderNodeViewModel("Home", home));
        foreach (var volume in volumes.Where(v => v.Kind == VolumeKind.Fixed))
            TreeRoots.Add(new FolderNodeViewModel(volume.Label, volume.RootPath));
    }

    /// <summary>In "Current folder" mode, re-roots the tree at the active pane's folder (if it changed).</summary>
    private void ReRootTreeToCurrent()
    {
        if (!IsTreeVisible || _settings.Current.TreeRoot != "Current")
            return;

        var path = ActiveTab?.CurrentPath;
        if (string.IsNullOrEmpty(path) || !_fileSystem.DirectoryExists(path))
        {
            TreeRoots.Clear();
            return;
        }
        if (TreeRoots.Count == 1 && string.Equals(TreeRoots[0].Path, path, StringComparison.Ordinal))
            return; // already rooted here

        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        TreeRoots.Clear();
        TreeRoots.Add(new FolderNodeViewModel(string.IsNullOrEmpty(name) ? path : name, path));
    }

    /// <summary>
    /// Renders volumes grouped by physical disk: a multi-partition disk gets a disk header with its
    /// partitions indented beneath it; a single-partition disk (or a diskless/GVfs mount) is shown as
    /// one row (PRD §6.10 — partitions belong to a drive, not a separate device).
    /// </summary>
    private void AddVolumeGroups(ObservableCollection<SidebarItemViewModel> target, List<DriveVolume> volumes, bool removableSection)
    {
        var rowKind = removableSection ? SidebarItemKind.Device : SidebarItemKind.Drive;

        // Group by physical disk; volumes without a disk (Windows letters, GVfs) each stand alone.
        foreach (var group in volumes.Where(v => v.Disk is not null).GroupBy(v => v.Disk).OrderBy(g => g.Key))
        {
            var partitions = group.OrderBy(v => v.Device, StringComparer.Ordinal).ToList();
            if (partitions.Count == 1)
            {
                target.Add(ToSidebar(partitions[0], rowKind));
                continue;
            }

            target.Add(new SidebarItemViewModel { Name = group.Key!, Kind = SidebarItemKind.Disk });
            foreach (var partition in partitions)
                target.Add(ToSidebar(partition, SidebarItemKind.Partition));
        }

        foreach (var volume in volumes.Where(v => v.Disk is null))
            target.Add(ToSidebar(volume, rowKind));
    }

    private SidebarItemViewModel ToSidebar(DriveVolume volume, SidebarItemKind kind) => new()
    {
        // Partitions label with the device leaf (e.g. "sda1") when the mount label is just its path.
        Name = kind == SidebarItemKind.Partition && volume.Device is { } d
            ? $"{System.IO.Path.GetFileName(d)} · {volume.Label}"
            : volume.Label,
        Path = volume.RootPath,
        Kind = kind,
        FreeBytes = volume.FreeBytes,
        TotalBytes = volume.TotalBytes,
        FileSystem = volume.FileSystem ?? (volume.Kind == VolumeKind.Gvfs ? "GVfs" : null),
        IsEjectable = volume.IsRemovable,
        Device = volume.Device,
        // Formatting is opt-in, needs a real block device, and never targets the running root mount.
        CanFormat = _settings.Current.EnableDiskFormatting && volume.Device is not null && volume.RootPath != "/",
        OpenCommand = OpenSidebarItemCommand,
        OpenInNewTabCommand = OpenSidebarInNewTabCommand,
        OpenInNewPaneCommand = OpenSidebarInNewPaneCommand,
        EjectCommand = EjectCommand,
        FormatCommand = FormatVolumeCommand,
    };

    private static string VolumeSignature(IReadOnlyList<DriveVolume> volumes) =>
        string.Join("|", volumes.Select(v => v.RootPath).OrderBy(p => p));

    [RelayCommand]
    private async Task Eject(SidebarItemViewModel? item)
    {
        if (item is { IsEjectable: true })
        {
            await _device.EjectAsync(item.Path);
            await LoadSidebarAsync();
        }
    }

    /// <summary>Set by the view to launch the format dialog for a device; true if a filesystem was created.</summary>
    public Func<SidebarItemViewModel, Task<bool>>? FormatRequester { get; set; }

    /// <summary>The format service, exposed so the view can build the format dialog (PRD §6.10).</summary>
    public IDiskService Disk => _disk;

    /// <summary>Filesystems offered in the format dialog: the installed set narrowed by the user's choice.</summary>
    public IReadOnlyList<FilesystemType> AllowedFilesystems()
    {
        var available = _disk.AvailableFilesystems();
        var wanted = _settings.Current.FormatFilesystems;
        return wanted.Count == 0 ? available : available.Where(f => wanted.Contains(f.Id)).ToList();
    }

    /// <summary>Context menu: format a drive/partition (creating a new filesystem) after confirmation.</summary>
    [RelayCommand]
    private async Task FormatVolume(SidebarItemViewModel? item)
    {
        if (item is not { CanFormat: true } || FormatRequester is null)
            return;
        if (await FormatRequester(item))
            await LoadSidebarAsync();
    }

    /// <summary>Polls for volume plug/unplug and refreshes the sidebar only when the set changes (PRD §6.10).</summary>
    private void StartDevicePolling()
    {
        _devicePoll ??= new Timer(async _ =>
        {
            try
            {
                var volumes = await _fileSystem.ListVolumesAsync();
                if (VolumeSignature(volumes) == _volumeSignature)
                    return;
                Post(() => _ = LoadSidebarAsync());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // transient enumeration error; try again next tick
            }
        }, null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
    }

    private void Post(Action action)
    {
        if (_sync is not null)
            _sync.Post(_ => action(), null);
        else
            action();
    }

    private IEnumerable<SidebarItemViewModel> BuildFavorites()
    {
        (string name, Environment.SpecialFolder folder)[] wanted =
        [
            ("Home", Environment.SpecialFolder.UserProfile),
            ("Desktop", Environment.SpecialFolder.DesktopDirectory),
            ("Documents", Environment.SpecialFolder.MyDocuments),
            ("Downloads", Environment.SpecialFolder.UserProfile), // Downloads has no SpecialFolder; derived below
        ];

        foreach (var (name, folder) in wanted)
        {
            if (_settings.Current.HiddenDefaultFavorites.Contains(name))
                continue; // the user removed this built-in pin

            var path = name == "Downloads"
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : Environment.GetFolderPath(folder);

            if (!string.IsNullOrEmpty(path) && _fileSystem.DirectoryExists(path))
                yield return new SidebarItemViewModel
                {
                    Name = name, Path = path, Kind = SidebarItemKind.Favorite,
                    OpenCommand = OpenSidebarItemCommand,
                    OpenInNewTabCommand = OpenSidebarInNewTabCommand,
                    OpenInNewPaneCommand = OpenSidebarInNewPaneCommand,
                    UnpinCommand = HideDefaultFavoriteCommand, // built-ins are removed by name, not path
                };
        }

        // User-pinned favorites persisted in settings (PRD §6.2, §6.8). Only these can be unpinned.
        foreach (var path in _settings.Current.Favorites)
        {
            if (!string.IsNullOrEmpty(path) && _fileSystem.DirectoryExists(path))
                yield return new SidebarItemViewModel
                {
                    Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : path,
                    Path = path,
                    Kind = SidebarItemKind.Favorite,
                    UnpinCommand = UnpinFavoriteCommand,
                    OpenCommand = OpenSidebarItemCommand,
                    OpenInNewTabCommand = OpenSidebarInNewTabCommand,
                    OpenInNewPaneCommand = OpenSidebarInNewPaneCommand,
                };
        }
    }

    [RelayCommand]
    private async Task UnpinFavorite(SidebarItemViewModel? item)
    {
        if (item is null || !_settings.Current.Favorites.Remove(item.Path))
            return;
        await _settings.SaveAsync();
        await LoadSidebarAsync();
    }

    /// <summary>Removes a built-in favorite (Home/Desktop/…) by name so it stays hidden across restarts.</summary>
    [RelayCommand]
    private async Task HideDefaultFavorite(SidebarItemViewModel? item)
    {
        if (item is null || _settings.Current.HiddenDefaultFavorites.Contains(item.Name))
            return;
        _settings.Current.HiddenDefaultFavorites.Add(item.Name);
        await _settings.SaveAsync();
        await LoadSidebarAsync();
    }

    [RelayCommand]
    private Task OpenSidebarItem(SidebarItemViewModel? item)
    {
        if (item is { IsNavigable: true } && ActiveTab is { } tab)
            return tab.NavigateToAsync(item.Path);
        return Task.CompletedTask;
    }

    /// <summary>Context menu: opens a sidebar target in a new tab in the active pane.</summary>
    [RelayCommand]
    private async Task OpenSidebarInNewTab(SidebarItemViewModel? item)
    {
        if (item is not { IsNavigable: true })
            return;
        var tab = CreateTab();
        Layout.AddTab(tab, Layout.ActivePane);
        await tab.NavigateToAsync(item.Path);
    }

    /// <summary>Context menu: opens a sidebar target in a new pane split to the right.</summary>
    [RelayCommand]
    private async Task OpenSidebarInNewPane(SidebarItemViewModel? item)
    {
        if (item is not { IsNavigable: true })
            return;
        var tab = CreateTab();
        var target = Layout.ActivePane ?? Layout.Panes().FirstOrDefault();
        if (target is not null)
            Layout.Split(tab, target, DockSide.Right);
        else
            Layout.AddTab(tab);
        await tab.NavigateToAsync(item.Path);
    }

    // ---- file operations ----

    private bool CanTransfer => IsDualPane;

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private void CopyToOther() => EnqueueTransfer(FileOperationKind.Copy);

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private void MoveToOther() => EnqueueTransfer(FileOperationKind.Move);

    private void EnqueueTransfer(FileOperationKind kind)
    {
        var sources = SelectedPaths(ActiveTab);
        var dest = OtherTab?.CurrentPath;
        if (sources.Count == 0 || string.IsNullOrEmpty(dest))
            return;

        OperationQueue.Enqueue(kind, sources, dest);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var sources = SelectedPaths(ActiveTab);
        foreach (var path in sources)
        {
            try
            {
                await _trash.TrashAsync(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
            {
                if (ActiveTab is { } t) t.StatusText = $"Delete failed: {ex.Message}";
            }
        }
        RefreshActiveTab();
    }

    [RelayCommand]
    private async Task NewFolderAsync()
    {
        if (ActiveTab?.CurrentPath is not { Length: > 0 } dir) return;
        await _operations.CreateFolderAsync(dir, "New folder");
        RefreshActiveTab();
    }

    [RelayCommand]
    private async Task NewFileAsync()
    {
        if (ActiveTab?.CurrentPath is not { Length: > 0 } dir) return;
        await _operations.CreateFileAsync(dir, "New file.txt");
        RefreshActiveTab();
    }

    [RelayCommand]
    private async Task RenameSelectedAsync()
    {
        if (ActiveTab?.SelectedEntry is not { } selected || NameRequester is null)
            return;

        var newName = await NameRequester(selected.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == selected.Name)
            return;

        try
        {
            await _operations.RenameAsync(selected.FullPath, newName.Trim());
            RefreshActiveTab();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ActiveTab.StatusText = $"Rename failed: {ex.Message}";
        }
    }

    // ---- tags, OS integration, favorites, batch rename, settings ----

    /// <summary>Set by the view to launch the batch-rename dialog; returns accepted proposals or null.</summary>
    public Func<IReadOnlyList<FileSystemEntry>, Task<IReadOnlyList<RenameProposal>?>>? BatchRenameRequester { get; set; }

    /// <summary>Set by the view to launch the settings dialog editing the given settings; true if applied.</summary>
    public Func<AppSettings, Task<bool>>? SettingsRequester { get; set; }

    /// <summary>Raised after settings load or change so the view can apply theme/accent/font.</summary>
    public event EventHandler? ThemeChanged;

    [RelayCommand]
    private async Task AssignTag(string? hex)
    {
        if (ActiveTab?.SelectedEntry is { } e)
        {
            await _tags.SetTagAsync(e.FullPath, hex);
            RefreshActiveTab();
        }
    }

    [RelayCommand]
    private Task ClearTag() => AssignTag(null);

    [RelayCommand]
    private void FilterByTag(string? hex)
    {
        if (ActiveTab is { } t)
            t.TagFilter = hex;
    }

    [RelayCommand]
    private void ClearTagFilter()
    {
        if (ActiveTab is { } t)
            t.TagFilter = null;
    }

    [RelayCommand]
    private Task OpenTerminalHere() =>
        ActiveTab?.CurrentPath is { Length: > 0 } dir ? _shell.OpenTerminalAsync(dir) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenSelectedExternally() =>
        ActiveTab?.SelectedEntry is { } e ? _shell.OpenAsync(e.FullPath) : Task.CompletedTask;

    [RelayCommand]
    private async Task PinFavorite()
    {
        if (ActiveTab?.CurrentPath is not { Length: > 0 } dir)
            return;
        if (!_settings.Current.Favorites.Contains(dir))
        {
            _settings.Current.Favorites.Add(dir);
            await _settings.SaveAsync();
            await LoadSidebarAsync();
        }
    }

    [RelayCommand]
    private async Task BatchRename()
    {
        if (BatchRenameRequester is null || ActiveTab is null)
            return;

        var entries = ActiveTab.Entries.Select(e => e.Entry).ToList();
        var proposals = await BatchRenameRequester(entries);
        if (proposals is null)
            return;

        foreach (var p in proposals.Where(p => p.Changed))
        {
            try
            {
                await _operations.RenameAsync(p.Entry.FullPath, p.ProposedName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ActiveTab.StatusText = $"Rename failed for {p.OriginalName}: {ex.Message}";
            }
        }
        RefreshActiveTab();
    }

    [RelayCommand]
    private async Task ExtractHere()
    {
        if (ActiveTab?.SelectedEntry is not { } e || !_archives.IsArchive(e.FullPath))
            return;

        var dest = FileOperationService.UniquePath(
            Path.Combine(ActiveTab.CurrentPath, Path.GetFileNameWithoutExtension(e.Name)));
        try
        {
            ActiveTab.StatusText = $"Extracting {e.Name}…";
            await _archives.ExtractAllAsync(e.FullPath, dest);
            RefreshActiveTab();
        }
        catch (Exception ex)
        {
            // Third-party format readers can throw arbitrary exceptions; keep the app alive.
            ActiveTab.StatusText = $"Extract failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void IdentifyFile()
    {
        if (ActiveTab?.SelectedEntry is { } e)
            ActiveTab.StatusText = $"{e.Name}: {_archives.Identify(e.FullPath) ?? "unrecognised format"}";
    }

    /// <summary>Alt+Enter: opens a window describing the selected item (PRD §6.1).</summary>
    [RelayCommand]
    private async Task ShowProperties()
    {
        if (ActiveTab?.SelectedEntry is { } e && PropertiesRequester is not null)
            await PropertiesRequester(e.Entry);
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        if (SettingsRequester is null)
            return;
        if (await SettingsRequester(_settings.Current))
        {
            await _settings.SaveAsync();
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            ApplyKeybinds();
            IsSearchBarVisible = _settings.Current.SearchBarVisible;
            _shell.TerminalCommand = _settings.Current.TerminalCommand;
            BuildToolbar(); // reflect any hide/show changes while preserving the saved order
            await LoadSidebarAsync();
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (ActiveTab?.SelectedEntry is { } e)
            ClipboardCopyRequested?.Invoke(this, e.FullPath);
    }

    [RelayCommand]
    private void CopyName()
    {
        if (ActiveTab?.SelectedEntry is { } e)
            ClipboardCopyRequested?.Invoke(this, e.Name);
    }

    // ---- helpers ----

    private static IReadOnlyList<string> SelectedPaths(FileTabViewModel? tab)
        => tab?.SelectedEntries is { Count: > 0 } items
            ? items.Select(e => e.FullPath).ToList()
            : tab?.SelectedEntry is { } e ? [e.FullPath] : [];

    private void RefreshActiveTab() => _ = ActiveTab?.RefreshCommand.ExecuteAsync(null);

    private void RefreshPanes()
    {
        foreach (var tab in AllTabs)
            _ = tab.RefreshCommand.ExecuteAsync(null);
    }
}
