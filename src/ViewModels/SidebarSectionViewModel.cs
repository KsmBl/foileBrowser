using System.Collections.ObjectModel;

namespace FoileBrowser.ViewModels;

/// <summary>
/// A draggable block in the navigation sidebar (PRD §6.2): a titled section holding either a list of
/// items (Favorites/Drives/Devices) or the folder-tree navigator. Sections can be reordered by
/// dragging their headers; the order is persisted.
/// </summary>
public sealed class SidebarSectionViewModel
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    /// <summary>True for the folder-tree section (its content is the tree, not <see cref="Items"/>).</summary>
    public bool IsTree { get; init; }

    public ObservableCollection<SidebarItemViewModel> Items { get; } = [];
}
