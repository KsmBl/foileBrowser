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

    [ObservableProperty]
    private FileTabViewModel? _activeTab;

    /// <summary>True when this is the focused pane whose selection drives operations (PRD §6.3).</summary>
    [ObservableProperty]
    private bool _isActive;

    public ObservableCollection<FileTabViewModel> Tabs { get; } = [];

    /// <summary>Raised when the user interacts with the pane so the window can mark it active.</summary>
    public event EventHandler? Activated;

    public PaneViewModel(IFileSystemService fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public FileTabViewModel AddTab(bool activate = true)
    {
        var tab = new FileTabViewModel(_fileSystem);
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
}
