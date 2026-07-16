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
    private readonly IDirectorySizeService _sizes;
    private readonly DisplayOptions _display = new();

    private FileTabViewModel? _observedTab;
    private CancellationTokenSource? _previewCts;
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    private Timer? _devicePoll;
    private string _volumeSignature = string.Empty;

    [ObservableProperty]
    private PaneViewModel _activePane;

    // Dockable layout: panes tiled in a horizontal proportional dock, each in its own document dock
    // so they sit side by side with a draggable splitter and can be re-docked/tabbed/floated (PRD §6.2).
    private readonly Factory _dockFactory = new();
    private ProportionalDock _paneArea = null!;
    private readonly List<PaneViewModel> _panes = [];
    private PaneViewModel? _lastOtherPane;

    [ObservableProperty]
    private IRootDock _layout = null!;

    public IFactory DockFactory => _dockFactory;

    /// <summary>True when more than one pane is open (enables cross-pane copy/move).</summary>
    public bool IsDualPane => _panes.Count > 1;

    /// <summary>The first two panes, kept as named handles for the classic side-by-side workflow.</summary>
    public PaneViewModel LeftPane { get; }
    public PaneViewModel RightPane { get; }
    public OperationQueueViewModel OperationQueue { get; }
    public CommandPaletteViewModel CommandPalette { get; }
    public ObservableCollection<SidebarItemViewModel> Sidebar { get; } = [];

    // Inspector panel state (PRD §6.5).
    [ObservableProperty]
    private PreviewResult? _preview;

    [ObservableProperty]
    private bool _isInspectorOpen = true;

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
        IArchiveService? archives = null, IDeviceService? device = null)
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
        _sizes = new DirectorySizeService();
        _search = search ??= new SearchService();

        LeftPane = CreatePane();
        RightPane = CreatePane();
        _activePane = LeftPane;
        LeftPane.IsActive = true;

        BuildLayout(LeftPane, RightPane);

        OperationQueue = new OperationQueueViewModel(operations);
        OperationQueue.OperationCompleted += (_, _) => RefreshPanes();

        CommandPalette = new CommandPaletteViewModel(BuildCommands());
    }

    private readonly ISearchService _search;

    /// <summary>Creates a pane, wires its activation/observation, and registers it in the pane set.</summary>
    private PaneViewModel CreatePane()
    {
        var pane = new PaneViewModel(_fileSystem, _search, _archives, _sizes, _display) { ConfigureTab = ConfigureTab };
        pane.Activated += OnPaneActivated;
        pane.PropertyChanged += OnPanePropertyChanged;
        _panes.Add(pane);
        return pane;
    }

    private void OnPaneActivated(object? sender, EventArgs e)
    {
        if (sender is PaneViewModel pane)
            SetActivePane(pane);
    }

    /// <summary>Wraps a pane in its own document dock (so panes tile side by side, not as tabs).</summary>
    private DocumentDock MakeDock(PaneViewModel pane)
    {
        var dock = new DocumentDock
        {
            Id = "Dock_" + pane.Id,
            Title = "Pane",
            IsCollapsable = true, // an emptied dock collapses so its space is reclaimed
            CanCreateDocument = true,
            VisibleDockables = _dockFactory.CreateList<IDockable>(pane),
            ActiveDockable = pane,
        };
        dock.CreateDocument = new RelayCommand(() => _ = AddPane());
        return dock;
    }

    /// <summary>Assembles the initial dock layout: panes side by side with a splitter between them.</summary>
    private void BuildLayout(params PaneViewModel[] panes)
    {
        var children = new List<IDockable>();
        foreach (var pane in panes)
        {
            if (children.Count > 0)
                children.Add(new ProportionalDockSplitter());
            children.Add(MakeDock(pane));
        }

        _paneArea = new ProportionalDock
        {
            Id = "PaneArea",
            Title = "Panes",
            Orientation = Orientation.Horizontal,
            VisibleDockables = _dockFactory.CreateList(children.ToArray()),
        };

        var root = _dockFactory.CreateRootDock();
        root.Id = "Root";
        root.Title = "Root";
        root.VisibleDockables = _dockFactory.CreateList<IDockable>(_paneArea);
        root.DefaultDockable = _paneArea;
        root.ActiveDockable = _paneArea;

        Layout = root;
        _dockFactory.InitLayout(root);

        // Track panes the user closes via the dock UI so IsDualPane / OtherPane stay correct.
        _dockFactory.DockableClosed += OnDockableClosed;
    }

    private void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (e.Dockable is PaneViewModel pane && _panes.Remove(pane))
        {
            pane.Activated -= OnPaneActivated;
            pane.PropertyChanged -= OnPanePropertyChanged;
            if (ReferenceEquals(_lastOtherPane, pane))
                _lastOtherPane = null;
            if (ReferenceEquals(ActivePane, pane) && _panes.Count > 0)
                SetActivePane(_panes[0]);
            NotifyPaneCountChanged();
        }
    }

    /// <summary>Opens a new pane, split to the right of the active one, and focuses it (View ▸ New Pane).</summary>
    [RelayCommand]
    private async Task AddPane()
    {
        var pane = CreatePane();
        SplitInPane(pane);
        _dockFactory.SetActiveDockable(pane);
        _dockFactory.SetFocusedDockable(Layout, pane);
        SetActivePane(pane);
        NotifyPaneCountChanged();
        await pane.InitializeAsync();
    }

    /// <summary>Docks a freshly-created pane to the right of the current panes.</summary>
    private void SplitInPane(PaneViewModel pane)
    {
        var target = (ActivePane.Owner as IDock)
            ?? _paneArea.VisibleDockables?.OfType<IDock>().LastOrDefault();
        if (target is not null)
            _dockFactory.SplitToDock(target, pane, DockOperation.Right);
        else
            _dockFactory.AddDockable(_paneArea, pane); // empty layout fallback
    }

    private void NotifyPaneCountChanged()
    {
        OnPropertyChanged(nameof(IsDualPane));
        CopyToOtherCommand.NotifyCanExecuteChanged();
        MoveToOtherCommand.NotifyCanExecuteChanged();
    }

    public AppSettings Settings => _settings.Current;

    public IReadOnlyList<TagColor> TagPalette => _tags.Palette;

    private void ConfigureTab(FileTabViewModel tab) => tab.TagLookup = _tags.GetTag;

    [RelayCommand]
    private void OpenCommandPalette() => CommandPalette.Open();

    /// <summary>Registers every palette-visible action (PRD §6.6). Tab-scoped commands resolve the active tab lazily.</summary>
    private IEnumerable<CommandItem> BuildCommands()
    {
        Task Tab(Func<FileTabViewModel, Task> action) => ActiveTab is { } t ? action(t) : Task.CompletedTask;

        return
        [
            new("file.newFolder", "New Folder", "File", "Ctrl+Shift+N", () => NewFolderCommand.ExecuteAsync(null)),
            new("file.newFile", "New File", "File", null, () => NewFileCommand.ExecuteAsync(null)),
            new("file.rename", "Rename…", "File", "F2", () => RenameSelectedCommand.ExecuteAsync(null)),
            new("file.delete", "Delete to Trash", "File", "Delete", () => DeleteSelectedCommand.ExecuteAsync(null)),
            new("file.copyToOther", "Copy to Other Pane", "File", "F6", () => { CopyToOtherCommand.Execute(null); return Task.CompletedTask; }),
            new("file.moveToOther", "Move to Other Pane", "File", "F7", () => { MoveToOtherCommand.Execute(null); return Task.CompletedTask; }),
            new("file.copyPath", "Copy Path", "File", null, () => { CopyPathCommand.Execute(null); return Task.CompletedTask; }),
            new("file.copyName", "Copy Name", "File", null, () => { CopyNameCommand.Execute(null); return Task.CompletedTask; }),
            new("nav.back", "Go Back", "Navigate", "Alt+Left", () => Tab(t => t.GoBackCommand.ExecuteAsync(null))),
            new("nav.forward", "Go Forward", "Navigate", "Alt+Right", () => Tab(t => t.GoForwardCommand.ExecuteAsync(null))),
            new("nav.up", "Go Up", "Navigate", "Alt+Up", () => Tab(t => t.GoUpCommand.ExecuteAsync(null))),
            new("nav.refresh", "Refresh", "Navigate", "F5", () => Tab(t => t.RefreshCommand.ExecuteAsync(null))),
            new("view.newPane", "New Pane", "View", null, () => AddPaneCommand.ExecuteAsync(null)),
            new("view.toggleDual", "Toggle Dual Pane", "View", null, () => { ToggleDualPaneCommand.Execute(null); return Task.CompletedTask; }),
            new("view.toggleInspector", "Toggle Inspector", "View", "Ctrl+I", () => { ToggleInspectorCommand.Execute(null); return Task.CompletedTask; }),
            new("view.toggleHidden", "Toggle Hidden Files", "View", null, () => Tab(t => { t.ShowHidden = !t.ShowHidden; return Task.CompletedTask; })),
            new("view.sizeUnit", "Cycle Size Units (KiB/KB/Bytes)", "View", null, () => CycleSizeUnitCommand.ExecuteAsync(null)),
            new("view.dateFormat", "Cycle Date Format (absolute/relative)", "View", null, () => CycleDateFormatCommand.ExecuteAsync(null)),
            new("tab.new", "New Tab", "Tab", "Ctrl+T", () => ActivePane.NewTabCommand.ExecuteAsync(null)),
            new("tab.close", "Close Tab", "Tab", "Ctrl+W", () => { ActivePane.CloseTabCommand.Execute(ActivePane.ActiveTab); return Task.CompletedTask; }),
            new("search.stop", "Stop Search", "Search", null, () => Tab(t => t.StopSearchCommand.ExecuteAsync(null))),
            new("file.batchRename", "Batch Rename…", "File", null, () => BatchRenameCommand.ExecuteAsync(null)),
            new("os.terminal", "Open Terminal Here", "System", null, () => OpenTerminalHereCommand.ExecuteAsync(null)),
            new("os.openWith", "Open with Default App", "System", null, () => OpenSelectedExternallyCommand.ExecuteAsync(null)),
            new("fav.pin", "Pin Current Folder", "Favorites", null, () => PinFavoriteCommand.ExecuteAsync(null)),
            new("tag.clear", "Clear Tag", "Tag", null, () => ClearTagCommand.ExecuteAsync(null)),
            new("tag.filterClear", "Clear Tag Filter", "Tag", null, () => { ClearTagFilterCommand.Execute(null); return Task.CompletedTask; }),
            new("app.settings", "Settings…", "App", null, () => OpenSettingsCommand.ExecuteAsync(null)),
            new("archive.extract", "Extract Archive Here", "Archive", null, () => ExtractHereCommand.ExecuteAsync(null)),
            new("archive.identify", "Identify File", "Archive", null, () => { IdentifyFileCommand.Execute(null); return Task.CompletedTask; }),
            .. _tags.Palette.Select(c => new CommandItem(
                $"tag.set.{c.Name}", $"Tag: {c.Name}", "Tag", null, () => AssignTagCommand.ExecuteAsync(c.Hex))),
            .. _tags.Palette.Select(c => new CommandItem(
                $"tag.filter.{c.Name}", $"Filter by Tag: {c.Name}", "Tag", null, () => { FilterByTagCommand.Execute(c.Hex); return Task.CompletedTask; })),
        ];
    }

    public FileTabViewModel? ActiveTab => ActivePane.ActiveTab;

    /// <summary>The transfer target for F6/F7: the most-recently-active <em>other</em> pane.</summary>
    private PaneViewModel? OtherPane =>
        _lastOtherPane is { } p && _panes.Contains(p) && !ReferenceEquals(p, ActivePane)
            ? p
            : _panes.FirstOrDefault(x => !ReferenceEquals(x, ActivePane));

    public async Task InitializeAsync()
    {
        await _settings.LoadAsync();
        IsInspectorOpen = _settings.Current.IsInspectorOpen;

        if (Enum.TryParse<SizeUnit>(_settings.Current.SizeUnit, out var unit))
            _display.SizeUnit = unit;
        if (Enum.TryParse<DateDisplay>(_settings.Current.DateFormat, out var date))
            _display.DateDisplay = date;
        UpdateDisplayLabels();

        await RestorePanesAsync(_settings.Current.Session);

        await LoadSidebarAsync();
        RewireInspector();
        StartDevicePolling();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rebuilds the open panes from the saved session (any number of panes, PRD §6.2).</summary>
    private async Task RestorePanesAsync(SessionLayout session)
    {
        // Prefer the N-pane list; fall back to the legacy Left/Right fields for old settings files.
        var panes = session.Panes.Count > 0
            ? session.Panes
            :
            [
                new PaneSession { Tabs = session.LeftTabs, ActiveIndex = session.LeftActiveIndex },
                new PaneSession { Tabs = session.RightTabs, ActiveIndex = session.RightActiveIndex },
            ];

        // Pane 0 → LeftPane, pane 1 → RightPane (both already created). Extra panes are added; a
        // single persisted pane collapses to just LeftPane.
        await LeftPane.RestoreAsync(panes[0].Tabs, panes[0].ActiveIndex);

        if (panes.Count >= 2)
            await RightPane.RestoreAsync(panes[1].Tabs, panes[1].ActiveIndex);
        else
            ClosePane(RightPane);

        for (var i = 2; i < panes.Count; i++)
        {
            var pane = CreatePane();
            SplitInPane(pane);
            SetActivePane(pane); // chain the next split to the right of this newest pane
            await pane.RestoreAsync(panes[i].Tabs, panes[i].ActiveIndex);
        }

        NotifyPaneCountChanged();
        SetActivePane(LeftPane);
    }

    /// <summary>Snapshots every open pane's tabs into settings and persists (called on window close).</summary>
    public Task SaveSessionAsync()
    {
        var paneSessions = _panes.Select(p =>
        {
            var snap = p.Snapshot();
            return new PaneSession { Tabs = snap.Paths, ActiveIndex = snap.ActiveIndex };
        }).ToList();

        var session = new SessionLayout { Panes = paneSessions };
        // Mirror the first two panes into the legacy fields so downgrades still restore something.
        if (paneSessions.Count > 0)
            (session.LeftTabs, session.LeftActiveIndex) = (paneSessions[0].Tabs, paneSessions[0].ActiveIndex);
        if (paneSessions.Count > 1)
            (session.RightTabs, session.RightActiveIndex) = (paneSessions[1].Tabs, paneSessions[1].ActiveIndex);

        _settings.Current.Session = session;
        _settings.Current.IsDualPane = IsDualPane;
        _settings.Current.IsInspectorOpen = IsInspectorOpen;
        return _settings.SaveAsync();
    }

    private void SetActivePane(PaneViewModel pane)
    {
        // Remember the previously-active pane as the default transfer target for F6/F7.
        if (!ReferenceEquals(ActivePane, pane))
            _lastOtherPane = ActivePane;

        ActivePane = pane;
        foreach (var p in _panes)
            p.IsActive = ReferenceEquals(p, pane);

        CopyToOtherCommand.NotifyCanExecuteChanged();
        MoveToOtherCommand.NotifyCanExecuteChanged();
        RewireInspector();
    }

    // ---- inspector / preview ----

    [RelayCommand]
    private void ToggleInspector() => IsInspectorOpen = !IsInspectorOpen;

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
        foreach (var pane in _panes)
            foreach (var tab in pane.Tabs)
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

    [RelayCommand]
    private void ShowAbout()
    {
        var version = typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString(3) ?? "dev";
        if (ActiveTab is { } t)
            t.StatusText = $"foileBrowser {version} — a fast, keyboard-first, cross-platform file browser.";
    }

    private void OnPanePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaneViewModel.ActiveTab) && ReferenceEquals(sender, ActivePane))
            RewireInspector();
    }

    /// <summary>Observes the active tab's selection so the inspector follows it.</summary>
    private void RewireInspector()
    {
        if (_observedTab is not null)
            _observedTab.PropertyChanged -= OnObservedTabPropertyChanged;
        _observedTab = ActiveTab;
        if (_observedTab is not null)
            _observedTab.PropertyChanged += OnObservedTabPropertyChanged;
        _ = UpdatePreviewAsync();
    }

    private void OnObservedTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileTabViewModel.SelectedEntry))
            _ = UpdatePreviewAsync();
    }

    private async Task UpdatePreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        var cts = _previewCts = new CancellationTokenSource();

        var selected = ActiveTab?.SelectedEntry;
        if (selected is null)
        {
            Preview = null;
            return;
        }

        try
        {
            var result = await _previewService.CreateAsync(selected.Entry, cts.Token);
            if (!cts.Token.IsCancellationRequested)
                Preview = result;
        }
        catch (OperationCanceledException)
        {
            // A newer selection superseded this preview.
        }
    }

    // ---- layout ----

    /// <summary>
    /// Convenience toggle: collapse to a single pane, or add a second one. (Beyond this, panes are
    /// added/closed/arranged freely through the dock UI and View ▸ New Pane.)
    /// </summary>
    [RelayCommand]
    private void ToggleDualPane()
    {
        if (_panes.Count > 1)
        {
            foreach (var extra in _panes.Where(p => !ReferenceEquals(p, LeftPane)).ToList())
                ClosePane(extra);
            SetActivePane(LeftPane);
        }
        else
        {
            _ = AddPane();
        }
    }

    private void ClosePane(PaneViewModel pane) => _dockFactory.CloseDockable(pane);

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
        foreach (var volume in volumes.Where(v => v.Kind == VolumeKind.Fixed))
            Sidebar.Add(ToSidebar(volume, SidebarItemKind.Drive));

        var removable = volumes.Where(v => v.IsRemovable).ToList();
        if (removable.Count > 0)
        {
            Sidebar.Add(new SidebarItemViewModel { Name = "Devices", Kind = SidebarItemKind.Header });
            foreach (var volume in removable)
                Sidebar.Add(ToSidebar(volume, SidebarItemKind.Device));
        }
    }

    private static SidebarItemViewModel ToSidebar(DriveVolume volume, SidebarItemKind kind) => new()
    {
        Name = volume.Label,
        Path = volume.RootPath,
        Kind = kind,
        FreeBytes = volume.FreeBytes,
        TotalBytes = volume.TotalBytes,
        FileSystem = volume.FileSystem ?? (volume.Kind == VolumeKind.Gvfs ? "GVfs" : null),
        IsEjectable = volume.IsRemovable,
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
            var path = name == "Downloads"
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                : Environment.GetFolderPath(folder);

            if (!string.IsNullOrEmpty(path) && _fileSystem.DirectoryExists(path))
                yield return new SidebarItemViewModel { Name = name, Path = path, Kind = SidebarItemKind.Favorite };
        }

        // User-pinned favorites persisted in settings (PRD §6.2, §6.8).
        foreach (var path in _settings.Current.Favorites)
        {
            if (!string.IsNullOrEmpty(path) && _fileSystem.DirectoryExists(path))
                yield return new SidebarItemViewModel
                {
                    Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : path,
                    Path = path,
                    Kind = SidebarItemKind.Favorite,
                };
        }
    }

    [RelayCommand]
    private Task OpenSidebarItem(SidebarItemViewModel? item)
    {
        if (item is { IsNavigable: true } && ActiveTab is { } tab)
            return tab.NavigateToAsync(item.Path);
        return Task.CompletedTask;
    }

    // ---- file operations ----

    private bool CanTransfer => IsDualPane;

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private void CopyToOther() => EnqueueTransfer(FileOperationKind.Copy);

    [RelayCommand(CanExecute = nameof(CanTransfer))]
    private void MoveToOther() => EnqueueTransfer(FileOperationKind.Move);

    private void EnqueueTransfer(FileOperationKind kind)
    {
        var sources = SelectedPaths(ActivePane);
        var dest = OtherPane?.ActiveTab?.CurrentPath;
        if (sources.Count == 0 || string.IsNullOrEmpty(dest))
            return;

        OperationQueue.Enqueue(kind, sources, dest);
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var sources = SelectedPaths(ActivePane);
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

    private static IReadOnlyList<string> SelectedPaths(PaneViewModel pane)
        => pane.ActiveTab?.SelectedEntry is { } e ? [e.FullPath] : [];

    private void RefreshActiveTab() => _ = ActiveTab?.RefreshCommand.ExecuteAsync(null);

    private void RefreshPanes()
    {
        foreach (var pane in _panes)
            _ = pane.ActiveTab?.RefreshCommand.ExecuteAsync(null);
    }
}
