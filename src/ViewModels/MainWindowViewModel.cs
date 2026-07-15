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

    [ObservableProperty]
    private PaneViewModel _activePane;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyToOtherCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveToOtherCommand))]
    private bool _isDualPane = true;

    public PaneViewModel LeftPane { get; }
    public PaneViewModel RightPane { get; }
    public OperationQueueViewModel OperationQueue { get; }
    public ObservableCollection<SidebarItemViewModel> Sidebar { get; } = [];

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
        IFileSystemService fileSystem, IFileOperationService operations, ITrashService trash)
    {
        _fileSystem = fileSystem;
        _operations = operations;
        _trash = trash;

        LeftPane = new PaneViewModel(fileSystem);
        RightPane = new PaneViewModel(fileSystem);
        _activePane = LeftPane;
        LeftPane.IsActive = true;

        LeftPane.Activated += (_, _) => SetActivePane(LeftPane);
        RightPane.Activated += (_, _) => SetActivePane(RightPane);

        OperationQueue = new OperationQueueViewModel(operations);
        OperationQueue.OperationCompleted += (_, _) => RefreshPanes();
    }

    public FileTabViewModel? ActiveTab => ActivePane.ActiveTab;

    private PaneViewModel OtherPane => ReferenceEquals(ActivePane, LeftPane) ? RightPane : LeftPane;

    public async Task InitializeAsync()
    {
        await LeftPane.InitializeAsync();
        await RightPane.InitializeAsync();
        await LoadSidebarAsync();
    }

    private void SetActivePane(PaneViewModel pane)
    {
        ActivePane = pane;
        LeftPane.IsActive = ReferenceEquals(pane, LeftPane);
        RightPane.IsActive = ReferenceEquals(pane, RightPane);
        CopyToOtherCommand.NotifyCanExecuteChanged();
        MoveToOtherCommand.NotifyCanExecuteChanged();
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
