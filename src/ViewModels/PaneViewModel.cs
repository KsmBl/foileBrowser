using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoileBrowser.Services;

namespace FoileBrowser.ViewModels;

/// <summary>
/// One side of the window: a strip of tabs over a shared file view (PRD §6.2 "Tabs per pane").
/// A pane always has at least one tab.
/// </summary>
public partial class PaneViewModel : ViewModelBase
{
    private readonly IFileSystemService _fileSystem;
    private readonly ISearchService _search;
    private readonly IArchiveService? _archives;
    private readonly IDirectorySizeService? _sizes;

    [ObservableProperty]
    private FileTabViewModel? _activeTab;

    /// <summary>True when this is the focused pane whose selection drives operations (PRD §6.3).</summary>
    [ObservableProperty]
    private bool _isActive;

    public ObservableCollection<FileTabViewModel> Tabs { get; } = [];

    /// <summary>Invoked on each newly created tab so the shell can wire it (e.g. tag lookup).</summary>
    public Action<FileTabViewModel>? ConfigureTab { get; set; }

    /// <summary>Raised when the user interacts with the pane so the window can mark it active.</summary>
    public event EventHandler? Activated;

    public PaneViewModel(
        IFileSystemService fileSystem, ISearchService? search = null,
        IArchiveService? archives = null, IDirectorySizeService? sizes = null)
    {
        _fileSystem = fileSystem;
        _search = search ?? new SearchService();
        _archives = archives;
        _sizes = sizes;
    }

    public FileTabViewModel AddTab(bool activate = true)
    {
        var tab = new FileTabViewModel(_fileSystem, _search, null, _archives, _sizes);
        ConfigureTab?.Invoke(tab);
        Tabs.Add(tab);
        if (activate || ActiveTab is null)
            ActiveTab = tab;
        return tab;
    }

    [RelayCommand]
    private async Task NewTabAsync()
    {
        var tab = AddTab();
        await tab.InitializeAsync();
    }

    [RelayCommand]
    private void CloseTab(FileTabViewModel? tab)
    {
        tab ??= ActiveTab;
        if (tab is null || Tabs.Count <= 1)
            return; // keep at least one tab open

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();

        if (ReferenceEquals(ActiveTab, tab))
            ActiveTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
    }

    [RelayCommand]
    private void Activate() => Activated?.Invoke(this, EventArgs.Empty);

    /// <summary>Creates the pane's first tab and navigates it to the start folder.</summary>
    public Task InitializeAsync()
    {
        var tab = AddTab();
        return tab.InitializeAsync();
    }

    /// <summary>Reopens the given tab paths (PRD §6.2 restore across restart), falling back to a default tab.</summary>
    public async Task RestoreAsync(IReadOnlyList<string> paths, int activeIndex)
    {
        var valid = paths.Where(p => !string.IsNullOrEmpty(p) && _fileSystem.DirectoryExists(p)).ToList();
        if (valid.Count == 0)
        {
            await InitializeAsync();
            return;
        }

        foreach (var path in valid)
        {
            var tab = AddTab(activate: false);
            await tab.NavigateToAsync(path);
        }
        ActiveTab = Tabs[Math.Clamp(activeIndex, 0, Tabs.Count - 1)];
    }

    /// <summary>Captures the open tab paths and active index for persistence.</summary>
    public (List<string> Paths, int ActiveIndex) Snapshot()
    {
        var paths = Tabs.Select(t => t.CurrentPath).Where(p => !string.IsNullOrEmpty(p)).ToList();
        var active = ActiveTab is null ? 0 : Math.Max(0, Tabs.IndexOf(ActiveTab));
        return (paths, active);
    }
}
