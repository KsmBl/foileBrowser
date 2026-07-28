using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FoileBrowser.Docking;

/// <summary>
/// A tab's content, as seen by the docking layout. Deliberately toolkit-agnostic — it only needs a
/// title (observable so the tab strip can follow renames) — so the same <see cref="DockLayout"/>
/// outlives whichever UI toolkit renders it, as the move to NativeForms showed.
/// </summary>
public interface IDockable : INotifyPropertyChanged
{
    string Title { get; }
}

public enum DockOrientation
{
    Horizontal,
    Vertical,
}

/// <summary>Where a dropped tab lands relative to a pane: an edge splits, the centre moves in.</summary>
public enum DockSide
{
    Left,
    Right,
    Top,
    Bottom,
    Center,
}

/// <summary>A node in the layout tree: either a <see cref="DockPane"/> (leaf) or a <see cref="DockSplit"/>.</summary>
public abstract partial class DockNode : ObservableObject
{
    /// <summary>Proportional size within the parent split (maps to a Grid star weight in the view).</summary>
    [ObservableProperty]
    private double _weight = 1;

    /// <summary>Owning split, or null for the layout root.</summary>
    public DockSplit? Parent { get; internal set; }
}

/// <summary>A leaf pane holding a stack of tabs, one of which is active.</summary>
public sealed partial class DockPane : DockNode
{
    public ObservableCollection<IDockable> Tabs { get; } = [];

    [ObservableProperty]
    private IDockable? _activeTab;
}

/// <summary>An internal node tiling its children horizontally or vertically with splitters between them.</summary>
public sealed partial class DockSplit : DockNode
{
    [ObservableProperty]
    private DockOrientation _orientation;

    public ObservableCollection<DockNode> Children { get; } = [];
}
