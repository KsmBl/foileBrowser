using CommunityToolkit.Mvvm.ComponentModel;

namespace FoileBrowser.Docking;

/// <summary>
/// The docking layout: a tree of <see cref="DockSplit"/>s and <see cref="DockPane"/>s plus the
/// operations that rearrange it (split, move, close, reorder). Pure model — no UI types — so it is
/// unit-testable and portable across toolkits. The view listens to <see cref="StructureChanged"/> to
/// rebuild and to the observable collections/properties for lighter updates.
/// </summary>
public sealed partial class DockLayout : ObservableObject
{
    [ObservableProperty]
    private DockNode _root;

    [ObservableProperty]
    private DockPane? _activePane;

    [ObservableProperty]
    private IDockable? _activeDockable;

    /// <summary>Raised after the tree shape changes (a split added/removed) so the view can rebuild.</summary>
    public event EventHandler? StructureChanged;

    /// <summary>Raised when a tab is removed from the layout, so the owner can dispose it.</summary>
    public event EventHandler<IDockable>? TabClosed;

    public DockLayout(DockNode root)
    {
        _root = root;
        FixParents(root, null);
        _activePane = Leaves(root).FirstOrDefault();
        _activeDockable = _activePane?.ActiveTab ?? _activePane?.Tabs.FirstOrDefault();
    }

    /// <summary>Every leaf pane, left-to-right / top-to-bottom.</summary>
    public IEnumerable<DockPane> Panes() => Leaves(Root);

    private static IEnumerable<DockPane> Leaves(DockNode node)
    {
        switch (node)
        {
            case DockPane pane:
                yield return pane;
                break;
            case DockSplit split:
                foreach (var child in split.Children)
                    foreach (var leaf in Leaves(child))
                        yield return leaf;
                break;
        }
    }

    /// <summary>The pane currently holding <paramref name="tab"/>, or null.</summary>
    public DockPane? PaneOf(IDockable tab) => Panes().FirstOrDefault(p => p.Tabs.Contains(tab));

    // ---- activation ----

    public void Activate(IDockable tab)
    {
        if (PaneOf(tab) is not { } pane)
            return;
        pane.ActiveTab = tab;
        ActivePane = pane;
        ActiveDockable = tab;
    }

    // ---- add / close ----

    /// <summary>Adds a tab to <paramref name="pane"/> (or the active pane) and activates it.</summary>
    public void AddTab(IDockable tab, DockPane? pane = null)
    {
        pane ??= ActivePane ?? Panes().First();
        pane.Tabs.Add(tab);
        Activate(tab);
    }

    /// <summary>Removes a tab; if that empties its pane, the pane is removed and the tree collapsed.</summary>
    public void CloseTab(IDockable tab)
    {
        if (PaneOf(tab) is not { } pane)
            return;

        var index = pane.Tabs.IndexOf(tab);
        pane.Tabs.Remove(tab);
        TabClosed?.Invoke(this, tab);

        if (pane.Tabs.Count > 0)
        {
            if (ReferenceEquals(pane.ActiveTab, tab))
                pane.ActiveTab = pane.Tabs[Math.Clamp(index, 0, pane.Tabs.Count - 1)];
            if (ReferenceEquals(ActiveDockable, tab))
                Activate(pane.ActiveTab!);
            return;
        }

        // Pane emptied: drop it and collapse the tree, then move focus to another pane.
        RebuildAfterStructureChange();
        var fallback = Panes().FirstOrDefault();
        ActivePane = fallback;
        ActiveDockable = fallback?.ActiveTab ?? fallback?.Tabs.FirstOrDefault();
        if (ActiveDockable is not null)
            Activate(ActiveDockable);
    }

    // ---- move / reorder ----

    /// <summary>Moves <paramref name="tab"/> into <paramref name="target"/> at <paramref name="index"/>
    /// (reorder when it's the same pane), collapsing an emptied source pane.</summary>
    public void MoveTab(IDockable tab, DockPane target, int index)
    {
        var source = PaneOf(tab);
        if (source is null)
            return;

        source.Tabs.Remove(tab);
        index = Math.Clamp(index, 0, target.Tabs.Count);
        target.Tabs.Insert(index, tab);
        target.ActiveTab = tab;

        var emptied = source.Tabs.Count == 0 && !ReferenceEquals(source, target);
        if (source.Tabs.Count > 0 && ReferenceEquals(source.ActiveTab, tab))
            source.ActiveTab = source.Tabs[0];

        if (emptied)
            RebuildAfterStructureChange();

        Activate(tab);
    }

    /// <summary>Splits <paramref name="target"/> along <paramref name="side"/>, putting <paramref name="tab"/>
    /// into a new pane there (or, for <see cref="DockSide.Center"/>, just moves it into the target).</summary>
    public void Split(IDockable tab, DockPane target, DockSide side)
    {
        if (side == DockSide.Center)
        {
            MoveTab(tab, target, target.Tabs.Count);
            return;
        }

        var source = PaneOf(tab);
        source?.Tabs.Remove(tab);
        if (source is not null && source.Tabs.Count > 0 && ReferenceEquals(source.ActiveTab, tab))
            source.ActiveTab = source.Tabs[0];

        var newPane = new DockPane();
        newPane.Tabs.Add(tab);
        newPane.ActiveTab = tab;

        var orientation = side is DockSide.Left or DockSide.Right ? DockOrientation.Horizontal : DockOrientation.Vertical;
        var before = side is DockSide.Left or DockSide.Top;
        InsertSibling(target, newPane, orientation, before);

        RebuildAfterStructureChange();
        Activate(tab);
    }

    private void InsertSibling(DockNode target, DockNode inserted, DockOrientation orientation, bool before)
    {
        var parent = target.Parent;
        if (parent is not null && parent.Orientation == orientation)
        {
            var idx = parent.Children.IndexOf(target);
            parent.Children.Insert(before ? idx : idx + 1, inserted);
            inserted.Parent = parent;
            inserted.Weight = target.Weight;
            return;
        }

        // Wrap the target in a new split of the requested orientation.
        var split = new DockSplit { Orientation = orientation, Weight = target.Weight };
        var indexInParent = parent?.Children.IndexOf(target) ?? -1;
        parent?.Children.Remove(target);

        target.Weight = 1;
        inserted.Weight = 1;
        if (before)
        {
            split.Children.Add(inserted);
            split.Children.Add(target);
        }
        else
        {
            split.Children.Add(target);
            split.Children.Add(inserted);
        }
        target.Parent = split;
        inserted.Parent = split;

        if (parent is not null)
        {
            parent.Children.Insert(indexInParent, split);
            split.Parent = parent;
        }
        else
        {
            Root = split;
            split.Parent = null;
        }
    }

    // ---- normalization ----

    /// <summary>Collapses empty panes and single-child splits, then fixes parent links and raises the event.</summary>
    private void RebuildAfterStructureChange()
    {
        Root = Normalize(Root) ?? new DockPane();
        FixParents(Root, null);
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static DockNode? Normalize(DockNode node)
    {
        if (node is not DockSplit split)
            return node;

        var kept = new List<DockNode>();
        foreach (var child in split.Children.ToList())
        {
            if (Normalize(child) is not { } normalized)
                continue;
            if (normalized is DockPane pane && pane.Tabs.Count == 0)
                continue; // drop emptied panes
            kept.Add(normalized);
        }

        if (kept.Count == 0)
            return null;
        if (kept.Count == 1)
            return kept[0]; // a split with one child is just that child

        split.Children.Clear();
        foreach (var node2 in kept)
            split.Children.Add(node2);
        return split;
    }

    private static void FixParents(DockNode node, DockSplit? parent)
    {
        node.Parent = parent;
        if (node is DockSplit split)
            foreach (var child in split.Children)
                FixParents(child, split);
    }
}
