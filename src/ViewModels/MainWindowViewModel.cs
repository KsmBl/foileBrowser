using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// The window shell: a dockable set of panes, a sidebar, and a background operation queue, plus the
/// cross-pane file-operation commands (PRD §6.2, §6.3). Panes are Dock documents, so any number can
/// be opened and arranged by splitting, tabbing, or floating them.
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
    private readonly IArchiveService _archives;
    private readonly IDeviceService _device;
    private readonly IDiskService _disk;
    private readonly IDirectorySizeService _sizes;
    private readonly DisplayOptions _display = new();

    private CancellationTokenSource? _previewCts;
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    private Timer? _devicePoll;
    private string _volumeSignature = string.Empty;

    // Dockable layout: each folder tab is a Dock document; a "pane" is a document dock. Tabs can be
    // dragged into a new pane, tabbed together, or floated into their own window (PRD §6.2).
    private readonly Factory _dockFactory = new();
    private ProportionalDock _tabArea = null!;
    private readonly List<FileTabViewModel> _tabs = [];
    private FileTabViewModel? _lastOtherTab;

    [ObservableProperty]
    private IRootDock _layout = null!;

    [ObservableProperty]
    private FileTabViewModel? _activeTab;

    public IFactory DockFactory => _dockFactory;

    /// <summary>Every open tab across all panes.</summary>
    public IReadOnlyList<FileTabViewModel> Tabs => _tabs;

    /// <summary>True when a tab exists in a different pane than the active one (enables copy/move).</summary>
    public bool IsDualPane => OtherTab is not null;

    public OperationQueueViewModel OperationQueue { get; }
    public CommandPaletteViewModel CommandPalette { get; }
    public ObservableCollection<SidebarItemViewModel> Sidebar { get; } = [];

    // Inspector panel state (PRD §6.5).
    [ObservableProperty]
    private PreviewResult? _preview;

    [ObservableProperty]
    private bool _isInspectorOpen = true;

    /// <summary>Whether the emoji operations toolbar is shown (View ▸ Toolbar, persisted).</summary>
    [ObservableProperty]
    private bool _isToolbarVisible = true;

    /// <summary>Ids of toolbar buttons hidden by the user; individual buttons bind their visibility to
    /// this set via <see cref="Views.ToolbarButtonVisibleConverter"/> (PRD §6.8).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _hiddenToolbarButtons = [];

    /// <summary>Whether every pane's filter/search bar is shown; when off it's revealed on demand by
    /// Ctrl+F for the session and collapsed again on Escape (PRD §6.4).</summary>
    [ObservableProperty]
    private bool _isSearchBarVisible = true;

    /// <summary>Escape from a revealed-on-demand search bar returns it to its configured state.</summary>
    public void CollapseSearchBar() => IsSearchBarVisible = _settings.Current.SearchBarVisible;

    /// <summary>Set by the view to prompt the user for a name (rename). Returns null if cancelled.</summary>
    public Func<string, Task<string?>>? NameRequester { get; set; }

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
        IArchiveService? archives = null, IDeviceService? device = null, IDiskService? disk = null)
    {
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
        _search = search ??= new SearchService();

        var first = CreateTab();
        var second = CreateTab();
        _activeTab = first;

        BuildLayout(first, second);

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

    /// <summary>Creates a folder tab (a dockable document) and registers it.</summary>
    private FileTabViewModel CreateTab()
    {
        var tab = new FileTabViewModel(_fileSystem, _search, _shell, _archives, _sizes, _display)
        {
            TagLookup = _tags.GetTag,
        };
        tab.PropertyChanged += OnTabPropertyChanged;
        _tabs.Add(tab);
        return tab;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileTabViewModel.SelectedEntry) && ReferenceEquals(sender, ActiveTab))
            _ = UpdatePreviewAsync();
    }

    /// <summary>Wraps tabs in a document dock (a "pane"); its "+" opens a new tab there.</summary>
    private DocumentDock MakeDock(params FileTabViewModel[] tabs)
    {
        var dock = new DocumentDock
        {
            Id = "Dock_" + Guid.NewGuid().ToString("N")[..8],
            Title = "Tabs",
            IsCollapsable = true, // an emptied dock collapses so its space is reclaimed
            CanCreateDocument = true,
            VisibleDockables = _dockFactory.CreateList<IDockable>(tabs),
            ActiveDockable = tabs[0],
        };
        dock.CreateDocument = new RelayCommand(() => _ = AddTabToDock(dock));
        return dock;
    }

    /// <summary>Initial layout: two panes side by side, each with one tab; wires factory events once.</summary>
    private void BuildLayout(params FileTabViewModel[] tabs)
    {
        RebuildLayout(tabs.Select(t => (new[] { t }, 0)).ToList());

        _dockFactory.DockableClosed += OnDockableClosed;
        _dockFactory.ActiveDockableChanged += (_, e) => { if (e.Dockable is FileTabViewModel t) SetActiveTab(t); };
        _dockFactory.FocusedDockableChanged += (_, e) => { if (e.Dockable is FileTabViewModel t) SetActiveTab(t); };
    }

    /// <summary>(Re)builds the pane layout: one document dock per pane, tiled horizontally with splitters.</summary>
    private void RebuildLayout(List<(FileTabViewModel[] Tabs, int Active)> panes)
    {
        var children = new List<IDockable>();
        foreach (var (tabs, active) in panes)
        {
            if (children.Count > 0)
                children.Add(new ProportionalDockSplitter());
            var dock = MakeDock(tabs);
            dock.ActiveDockable = tabs[Math.Clamp(active, 0, tabs.Length - 1)];
            children.Add(dock);
        }

        _tabArea = new ProportionalDock
        {
            Id = "TabArea",
            Title = "Tabs",
            Orientation = Orientation.Horizontal,
            VisibleDockables = _dockFactory.CreateList(children.ToArray()),
        };

        var root = _dockFactory.CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        root.VisibleDockables = _dockFactory.CreateList<IDockable>(_tabArea);
        root.DefaultDockable = _tabArea;
        root.ActiveDockable = _tabArea;

        Layout = root;
        _dockFactory.InitLayout(root);
    }

    private void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (e.Dockable is not FileTabViewModel tab || !_tabs.Remove(tab))
            return;

        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.Dispose();
        if (ReferenceEquals(_lastOtherTab, tab))
            _lastOtherTab = null;
        if (ReferenceEquals(ActiveTab, tab))
            SetActiveTab(_tabs.FirstOrDefault());
        NotifyPaneCountChanged();
    }

    /// <summary>Ctrl+T / a pane's "+": opens a new tab in the active pane (dock).</summary>
    [RelayCommand]
    private Task AddTab() => AddTabToDock(ActiveTab?.Owner as IDocumentDock);

    private async Task AddTabToDock(IDocumentDock? dock)
    {
        var tab = CreateTab();
        dock ??= CollectDocks().FirstOrDefault();
        if (dock is not null)
            _dockFactory.AddDockable(dock, tab);
        else
            AddNewDock(MakeDock(tab)); // no panes remain
        Focus(tab);
        await tab.InitializeAsync();
    }

    /// <summary>New Pane: opens a new tab split into its own pane to the right, and focuses it.</summary>
    [RelayCommand]
    private async Task AddPane()
    {
        var tab = CreateTab();
        var target = (ActiveTab?.Owner as IDock) ?? _tabArea.VisibleDockables?.OfType<IDock>().LastOrDefault();
        if (target is not null)
            _dockFactory.SplitToDock(target, tab, DockOperation.Right);
        else
            AddNewDock(MakeDock(tab)); // no panes remain
        Focus(tab);
        await tab.InitializeAsync();
    }

    private void AddNewDock(IDock dock)
    {
        var items = _tabArea.VisibleDockables ??= _dockFactory.CreateList<IDockable>();
        if (items.Count > 0)
            items.Add(new ProportionalDockSplitter());
        items.Add(dock);
        _dockFactory.InitDockable(dock, _tabArea);
    }

    private void Focus(FileTabViewModel tab)
    {
        _dockFactory.SetActiveDockable(tab);
        _dockFactory.SetFocusedDockable(Layout, tab);
        SetActiveTab(tab);
        NotifyPaneCountChanged();
    }

    /// <summary>Called by a tab's view when it gains focus, so operations target it.</summary>
    public void ActivateTab(FileTabViewModel tab)
    {
        if (ReferenceEquals(ActiveTab, tab))
            return;
        _dockFactory.SetActiveDockable(tab);
        SetActiveTab(tab);
    }

    private void SetActiveTab(FileTabViewModel? tab)
    {
        // Remember the previously-active tab (if in another pane) as the F6/F7 transfer target.
        if (ActiveTab is { } prev && tab is not null && !SameDock(prev, tab))
            _lastOtherTab = prev;
        ActiveTab = tab;
    }

    partial void OnActiveTabChanged(FileTabViewModel? oldValue, FileTabViewModel? newValue)
    {
        NotifyPaneCountChanged();
        RewireInspector();
    }

    private static bool SameDock(FileTabViewModel a, FileTabViewModel b) => ReferenceEquals(a.Owner, b.Owner);

    /// <summary>Transfer target for F6/F7: the most-recently-active tab in a different pane.</summary>
    private FileTabViewModel? OtherTab
    {
        get
        {
            if (ActiveTab is not { } a)
                return null;
            if (_lastOtherTab is { } p && _tabs.Contains(p) && !SameDock(p, a))
                return p;
            return _tabs.FirstOrDefault(t => !ReferenceEquals(t, a) && !SameDock(t, a));
        }
    }

    /// <summary>Every document dock in the layout, recursing through splits.</summary>
    private List<IDocumentDock> CollectDocks()
    {
        var result = new List<IDocumentDock>();
        void Walk(IDockable? node)
        {
            if (node is IDocumentDock doc)
                result.Add(doc);
            if (node is IDock dock && dock.VisibleDockables is { } kids)
                foreach (var kid in kids)
                    Walk(kid);
        }
        Walk(_tabArea);
        return result;
    }

    private void NotifyPaneCountChanged()
    {
        OnPropertyChanged(nameof(IsDualPane));
        CopyToOtherCommand.NotifyCanExecuteChanged();
        MoveToOtherCommand.NotifyCanExecuteChanged();
    }

    public AppSettings Settings => _settings.Current;

    public IReadOnlyList<TagColor> TagPalette => _tags.Palette;

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

    /// <summary>Closes the active tab (Ctrl+W); Dock keeps at least one document per dock.</summary>
    [RelayCommand]
    private void CloseTab()
    {
        if (ActiveTab is { } tab)
            _dockFactory.CloseDockable(tab);
    }

    public async Task InitializeAsync()
    {
        await _settings.LoadAsync();
        IsInspectorOpen = _settings.Current.IsInspectorOpen;
        IsToolbarVisible = _settings.Current.IsToolbarVisible;
        HiddenToolbarButtons = _settings.Current.HiddenToolbarButtons.ToList();
        IsSearchBarVisible = _settings.Current.SearchBarVisible;

        if (Enum.TryParse<SizeUnit>(_settings.Current.SizeUnit, out var unit))
            _display.SizeUnit = unit;
        if (Enum.TryParse<DateDisplay>(_settings.Current.DateFormat, out var date))
            _display.DateDisplay = date;
        UpdateDisplayLabels();

        await RestoreTabsAsync(_settings.Current.Session);

        await LoadSidebarAsync();
        RewireInspector();
        StartDevicePolling();
        ApplyKeybinds();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rebuilds the panes (document docks) and their tabs from the saved session (PRD §6.2).</summary>
    private async Task RestoreTabsAsync(SessionLayout session)
    {
        var sessions = session.Panes.Count > 0
            ? session.Panes
            :
            [
                new PaneSession { Tabs = session.LeftTabs, ActiveIndex = session.LeftActiveIndex },
                new PaneSession { Tabs = session.RightTabs, ActiveIndex = session.RightActiveIndex },
            ];

        // Discard the placeholder tabs the constructor created.
        foreach (var placeholder in _tabs.ToList())
        {
            placeholder.PropertyChanged -= OnTabPropertyChanged;
            placeholder.Dispose();
        }
        _tabs.Clear();

        var panes = new List<(FileTabViewModel[] Tabs, int Active)>();
        foreach (var pane in sessions)
        {
            var valid = pane.Tabs.Where(p => !string.IsNullOrEmpty(p) && _fileSystem.DirectoryExists(p)).ToList();
            var tabs = new List<FileTabViewModel>();
            if (valid.Count == 0)
            {
                var tab = CreateTab();
                await tab.InitializeAsync();
                tabs.Add(tab);
            }
            else
            {
                foreach (var path in valid)
                {
                    var tab = CreateTab();
                    await tab.NavigateToAsync(path);
                    tabs.Add(tab);
                }
            }
            panes.Add((tabs.ToArray(), pane.ActiveIndex));
        }

        if (panes.Count == 0)
        {
            var tab = CreateTab();
            await tab.InitializeAsync();
            panes.Add(([tab], 0));
        }

        RebuildLayout(panes);
        var (firstTabs, firstActive) = panes[0];
        ActiveTab = firstTabs[Math.Clamp(firstActive, 0, firstTabs.Length - 1)];
        _dockFactory.SetActiveDockable(ActiveTab);
        NotifyPaneCountChanged();
    }

    /// <summary>Snapshots every pane's open tabs into settings and persists (called on window close).</summary>
    public Task SaveSessionAsync()
    {
        var paneSessions = CollectDocks()
            .Select(dock =>
            {
                var tabs = dock.VisibleDockables?.OfType<FileTabViewModel>().ToList() ?? [];
                var active = dock.ActiveDockable as FileTabViewModel;
                return new PaneSession
                {
                    Tabs = tabs.Select(t => t.CurrentPath).Where(p => !string.IsNullOrEmpty(p)).ToList(),
                    ActiveIndex = active is not null ? Math.Max(0, tabs.IndexOf(active)) : 0,
                };
            })
            .Where(s => s.Tabs.Count > 0)
            .ToList();

        var session = new SessionLayout { Panes = paneSessions };
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
    }

    private void RefreshAllDisplays()
    {
        foreach (var tab in _tabs)
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
        Sidebar.Clear();
        Sidebar.Add(new SidebarItemViewModel { Name = "Favorites", Kind = SidebarItemKind.Header });
        foreach (var fav in BuildFavorites())
            Sidebar.Add(fav);

        var volumes = await _fileSystem.ListVolumesAsync();
        _volumeSignature = VolumeSignature(volumes);

        Sidebar.Add(new SidebarItemViewModel { Name = "Drives", Kind = SidebarItemKind.Header });
        AddVolumeGroups(volumes.Where(v => v.Kind == VolumeKind.Fixed).ToList(), removableSection: false);

        var removable = volumes.Where(v => v.IsRemovable).ToList();
        if (removable.Count > 0)
        {
            Sidebar.Add(new SidebarItemViewModel { Name = "Devices", Kind = SidebarItemKind.Header });
            AddVolumeGroups(removable, removableSection: true);
        }
    }

    /// <summary>
    /// Renders volumes grouped by physical disk: a multi-partition disk gets a disk header with its
    /// partitions indented beneath it; a single-partition disk (or a diskless/GVfs mount) is shown as
    /// one row (PRD §6.10 — partitions belong to a drive, not a separate device).
    /// </summary>
    private void AddVolumeGroups(List<DriveVolume> volumes, bool removableSection)
    {
        var rowKind = removableSection ? SidebarItemKind.Device : SidebarItemKind.Drive;

        // Group by physical disk; volumes without a disk (Windows letters, GVfs) each stand alone.
        foreach (var group in volumes.Where(v => v.Disk is not null).GroupBy(v => v.Disk).OrderBy(g => g.Key))
        {
            var partitions = group.OrderBy(v => v.Device, StringComparer.Ordinal).ToList();
            if (partitions.Count == 1)
            {
                Sidebar.Add(ToSidebar(partitions[0], rowKind));
                continue;
            }

            Sidebar.Add(new SidebarItemViewModel { Name = group.Key!, Kind = SidebarItemKind.Disk });
            foreach (var partition in partitions)
                Sidebar.Add(ToSidebar(partition, SidebarItemKind.Partition));
        }

        foreach (var volume in volumes.Where(v => v.Disk is null))
            Sidebar.Add(ToSidebar(volume, rowKind));
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

    /// <summary>Restores all previously-removed built-in favorites (offered in Settings ▸ Sidebar).</summary>
    public async Task RestoreDefaultFavoritesAsync()
    {
        if (_settings.Current.HiddenDefaultFavorites.Count == 0)
            return;
        _settings.Current.HiddenDefaultFavorites.Clear();
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
        var dock = (ActiveTab?.Owner as IDocumentDock) ?? CollectDocks().FirstOrDefault();
        if (dock is not null)
            _dockFactory.AddDockable(dock, tab);
        else
            AddNewDock(MakeDock(tab));
        Focus(tab);
        await tab.NavigateToAsync(item.Path);
    }

    /// <summary>Context menu: opens a sidebar target in a new pane split to the right.</summary>
    [RelayCommand]
    private async Task OpenSidebarInNewPane(SidebarItemViewModel? item)
    {
        if (item is not { IsNavigable: true })
            return;
        var tab = CreateTab();
        var target = (ActiveTab?.Owner as IDock) ?? _tabArea.VisibleDockables?.OfType<IDock>().LastOrDefault();
        if (target is not null)
            _dockFactory.SplitToDock(target, tab, DockOperation.Right);
        else
            AddNewDock(MakeDock(tab));
        Focus(tab);
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
            HiddenToolbarButtons = _settings.Current.HiddenToolbarButtons.ToList();
            IsSearchBarVisible = _settings.Current.SearchBarVisible;
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
        => tab?.SelectedEntry is { } e ? [e.FullPath] : [];

    private void RefreshActiveTab() => _ = ActiveTab?.RefreshCommand.ExecuteAsync(null);

    private void RefreshPanes()
    {
        foreach (var tab in _tabs)
            _ = tab.RefreshCommand.ExecuteAsync(null);
    }
}
