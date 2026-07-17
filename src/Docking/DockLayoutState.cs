namespace FoileBrowser.Docking;

/// <summary>
/// Serializable snapshot of a <see cref="DockLayout"/> tree (for session save/restore). Plain mutable
/// properties keep JSON (de)serialisation trivial; tabs are stored as string keys the app resolves
/// (e.g. a folder path).
/// </summary>
public sealed class DockNodeState
{
    /// <summary>"pane" or "split".</summary>
    public string Kind { get; set; } = "pane";

    public double Weight { get; set; } = 1;

    // split
    public string? Orientation { get; set; }
    public List<DockNodeState> Children { get; set; } = [];

    // pane
    public List<string> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }

    /// <summary>Captures a layout tree into a serialisable snapshot using <paramref name="keyOf"/> per tab.</summary>
    public static DockNodeState Capture(DockNode node, Func<IDockable, string?> keyOf)
    {
        if (node is DockSplit split)
        {
            var state = new DockNodeState
            {
                Kind = "split",
                Weight = node.Weight,
                Orientation = split.Orientation.ToString(),
            };
            foreach (var child in split.Children)
                state.Children.Add(Capture(child, keyOf));
            return state;
        }

        var pane = (DockPane)node;
        var paneState = new DockNodeState { Kind = "pane", Weight = node.Weight };
        foreach (var tab in pane.Tabs)
            if (keyOf(tab) is { } key)
                paneState.Tabs.Add(key);
        paneState.ActiveIndex = pane.ActiveTab is null ? 0 : Math.Max(0, pane.Tabs.IndexOf(pane.ActiveTab));
        return paneState;
    }

    /// <summary>
    /// Rebuilds a layout from a snapshot. <paramref name="createTab"/> makes a fresh tab for each stored
    /// key (returning null to skip it). Returns null if nothing usable could be restored.
    /// </summary>
    public static DockLayout? Restore(DockNodeState state, Func<string, IDockable?> createTab)
    {
        var root = Build(state, createTab);
        return root is null ? null : new DockLayout(root);
    }

    private static DockNode? Build(DockNodeState state, Func<string, IDockable?> createTab)
    {
        if (state.Kind == "split")
        {
            var split = new DockSplit
            {
                Orientation = Enum.TryParse<DockOrientation>(state.Orientation, out var o) ? o : DockOrientation.Horizontal,
                Weight = state.Weight,
            };
            foreach (var child in state.Children)
                if (Build(child, createTab) is { } node)
                {
                    node.Parent = split;
                    split.Children.Add(node);
                }

            return split.Children.Count switch
            {
                0 => null,
                1 => split.Children[0],
                _ => split,
            };
        }

        var pane = new DockPane { Weight = state.Weight };
        foreach (var key in state.Tabs)
            if (createTab(key) is { } tab)
                pane.Tabs.Add(tab);
        if (pane.Tabs.Count == 0)
            return null;
        pane.ActiveTab = pane.Tabs[Math.Clamp(state.ActiveIndex, 0, pane.Tabs.Count - 1)];
        return pane;
    }
}
