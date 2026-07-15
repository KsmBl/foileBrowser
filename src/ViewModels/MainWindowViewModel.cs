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

    private FileTabViewModel? _observedTab;
    private CancellationTokenSource? _previewCts;

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
        : this(new FileSystemService(), new FileOperationService(), new TrashService(), new SearchService(), new PreviewService())
    {
    }

    public MainWindowViewModel(
        IFileSystemService fileSystem, IFileOperationService operations, ITrashService trash,
        ISearchService? search = null, IPreviewService? preview = null)
    {
        _fileSystem = fileSystem;
        _operations = operations;
        _trash = trash;
        _previewService = preview ?? new PreviewService();
        search ??= new SearchService();

        LeftPane = new PaneViewModel(fileSystem, search);
        RightPane = new PaneViewModel(fileSystem, search);
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
        ];
    }

    public FileTabViewModel? ActiveTab => ActivePane.ActiveTab;

    private PaneViewModel OtherPane => ReferenceEquals(ActivePane, LeftPane) ? RightPane : LeftPane;

    public async Task InitializeAsync()
    {
        await LeftPane.InitializeAsync();
        await RightPane.InitializeAsync();
        await LoadSidebarAsync();
        RewireInspector();
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

        Sidebar.Add(new SidebarItemViewModel { Name = "Drives", Kind = SidebarItemKind.Header });
        foreach (var volume in await _fileSystem.ListVolumesAsync())
        {
            Sidebar.Add(new SidebarItemViewModel
            {
                Name = volume.Label,
                Path = volume.RootPath,
                Kind = SidebarItemKind.Drive,
                FreeBytes = volume.FreeBytes,
                TotalBytes = volume.TotalBytes,
            });
        }
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
