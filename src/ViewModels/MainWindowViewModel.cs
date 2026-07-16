using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// The window shell: two panes of tabs, a sidebar, and a background operation queue,
/// plus the cross-pane file-operation commands (PRD §6.2, §6.3).
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

    private FileTabViewModel? _observedTab;
    private CancellationTokenSource? _previewCts;
    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    private Timer? _devicePoll;
    private string _volumeSignature = string.Empty;

    [ObservableProperty]
    private PaneViewModel _activePane;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyToOtherCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToOtherCommand))]
    private bool _isDualPane = true;

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
        search ??= new SearchService();

        LeftPane = new PaneViewModel(fileSystem, search, _archives, _sizes) { ConfigureTab = ConfigureTab };
        RightPane = new PaneViewModel(fileSystem, search, _archives, _sizes) { ConfigureTab = ConfigureTab };
        _activePane = LeftPane;
        LeftPane.IsActive = true;

        LeftPane.Activated += (_, _) => SetActivePane(LeftPane);
        RightPane.Activated += (_, _) => SetActivePane(RightPane);
        LeftPane.PropertyChanged += OnPanePropertyChanged;
        RightPane.PropertyChanged += OnPanePropertyChanged;

        OperationQueue = new OperationQueueViewModel(operations);
        OperationQueue.OperationCompleted += (_, _) => RefreshPanes();

        CommandPalette = new CommandPaletteViewModel(BuildCommands());
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
            new("view.toggleDual", "Toggle Dual Pane", "View", null, () => { ToggleDualPaneCommand.Execute(null); return Task.CompletedTask; }),
            new("view.toggleInspector", "Toggle Inspector", "View", "Ctrl+I", () => { ToggleInspectorCommand.Execute(null); return Task.CompletedTask; }),
            new("view.toggleHidden", "Toggle Hidden Files", "View", null, () => Tab(t => { t.ShowHidden = !t.ShowHidden; return Task.CompletedTask; })),
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

    private PaneViewModel OtherPane => ReferenceEquals(ActivePane, LeftPane) ? RightPane : LeftPane;

    public async Task InitializeAsync()
    {
        await _settings.LoadAsync();
        IsDualPane = _settings.Current.IsDualPane;
        IsInspectorOpen = _settings.Current.IsInspectorOpen;

        var session = _settings.Current.Session;
        await LeftPane.RestoreAsync(session.LeftTabs, session.LeftActiveIndex);
        await RightPane.RestoreAsync(session.RightTabs, session.RightActiveIndex);

        await LoadSidebarAsync();
        RewireInspector();
        StartDevicePolling();
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Snapshots the session and layout into settings and persists (called on window close).</summary>
    public Task SaveSessionAsync()
    {
        var left = LeftPane.Snapshot();
        var right = RightPane.Snapshot();
        _settings.Current.Session = new SessionLayout
        {
            LeftTabs = left.Paths,
            LeftActiveIndex = left.ActiveIndex,
            RightTabs = right.Paths,
            RightActiveIndex = right.ActiveIndex,
        };
        _settings.Current.IsDualPane = IsDualPane;
        _settings.Current.IsInspectorOpen = IsInspectorOpen;
        return _settings.SaveAsync();
    }

    private void SetActivePane(PaneViewModel pane)
    {
        ActivePane = pane;
        LeftPane.IsActive = ReferenceEquals(pane, LeftPane);
        RightPane.IsActive = ReferenceEquals(pane, RightPane);
        CopyToOtherCommand.NotifyCanExecuteChanged();
        MoveToOtherCommand.NotifyCanExecuteChanged();
        RewireInspector();
    }

    // ---- inspector / preview ----

    [RelayCommand]
    private void ToggleInspector() => IsInspectorOpen = !IsInspectorOpen;

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

    [RelayCommand]
    private void ToggleDualPane()
    {
        IsDualPane = !IsDualPane;
        if (!IsDualPane)
            SetActivePane(LeftPane);
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
        var dest = OtherPane.ActiveTab?.CurrentPath;
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
        _ = LeftPane.ActiveTab?.RefreshCommand.ExecuteAsync(null);
        _ = RightPane.ActiveTab?.RefreshCommand.ExecuteAsync(null);
    }
}
