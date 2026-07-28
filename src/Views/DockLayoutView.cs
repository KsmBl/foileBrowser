using FoileBrowser.Docking;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// Renders a <see cref="DockLayout"/> (PRD §6.2): splits become nested
/// <see cref="SplitContainer"/>s carrying the model's weights, and each pane is a
/// <see cref="TabControl"/> over its tabs — or, when the whole layout is one tab, the pane content
/// with no strip at all. The pane views are cached per tab so a rebuild keeps their scroll position
/// and selection.
/// </summary>
public sealed class DockLayoutView : Panel
{
    private readonly MainWindowViewModel _vm;
    private readonly Dictionary<FileTabViewModel, PaneView> _panes = [];
    private readonly List<Action> _cleanup = [];

    /// <summary>Splitters whose distance is a share of the container, re-applied on every resize.</summary>
    private readonly List<(SplitContainer Split, double Proportion)> _proportions = [];

    private int _rowHeight = 24;
    private bool _rebuildQueued;

    public DockLayoutView(MainWindowViewModel viewModel)
    {
        _vm = viewModel;
        Ui.Watch(_vm, this.OnLayoutReplaced, nameof(MainWindowViewModel.Layout));
    }

    /// <summary>Row density for every pane's file list (PRD §6.8).</summary>
    public int RowHeight
    {
        get => _rowHeight;
        set
        {
            if (_rowHeight == value)
                return;
            _rowHeight = value;
            foreach (var pane in _panes.Values)
                pane.RowHeight = value;
        }
    }

    private DockLayout? _layout;

    private void OnLayoutReplaced()
    {
        if (ReferenceEquals(_layout, _vm.Layout))
            return;

        if (_layout is not null)
            _layout.StructureChanged -= this.OnStructureChanged;
        _layout = _vm.Layout;
        if (_layout is not null)
            _layout.StructureChanged += this.OnStructureChanged;
        this.Rebuild();
    }

    private void OnStructureChanged(object? sender, EventArgs e) => this.QueueRebuild();

    /// <summary>
    /// Defers a rebuild to the next loop turn. Tab closes and splits arrive from inside a control's
    /// own event, and disposing that control's tree underneath it is not safe.
    /// </summary>
    private void QueueRebuild()
    {
        if (_rebuildQueued)
            return;
        _rebuildQueued = true;

        try
        {
            this.BeginInvoke(() =>
            {
                _rebuildQueued = false;
                this.Rebuild();
            });
        }
        catch (InvalidOperationException)
        {
            _rebuildQueued = false;
            this.Rebuild(); // no loop yet — nothing is realized, so rebuilding inline is safe
        }
    }

    private void Rebuild()
    {
        foreach (var undo in _cleanup)
            undo();
        _cleanup.Clear();
        _proportions.Clear();
        this.Controls.Clear();

        if (_layout is null)
            return;

        this.DropUnusedPanes();

        var content = this.BuildNode(_layout.Root);
        content.Dock = DockStyle.Fill;
        this.Controls.Add(content);
        this.PerformLayout();
        this.ApplyProportions();
    }

    /// <summary>Disposes the cached views of tabs the layout no longer holds.</summary>
    private void DropUnusedPanes()
    {
        var live = _layout!.Panes().SelectMany(p => p.Tabs).OfType<FileTabViewModel>().ToHashSet();
        foreach (var tab in _panes.Keys.Where(t => !live.Contains(t)).ToList())
        {
            _panes[tab].Detach();
            _panes.Remove(tab);
        }
    }

    // ---- tree ----

    private Control BuildNode(DockNode node) =>
        node is DockSplit split ? this.BuildSplit(split, 0) : this.BuildPane((DockPane)node);

    /// <summary>
    /// Renders children <paramref name="index"/>… as a right-folded chain of two-panel splitters,
    /// since the toolkit's splitter is binary while a <see cref="DockSplit"/> may hold any number.
    /// </summary>
    private Control BuildSplit(DockSplit split, int index)
    {
        if (index >= split.Children.Count - 1)
            return this.BuildNode(split.Children[index]);

        var first = split.Children[index];
        var container = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = split.Orientation == DockOrientation.Horizontal
                ? Orientation.Vertical   // a horizontal split puts panes side by side
                : Orientation.Horizontal,
            Panel1MinSize = 120,
            Panel2MinSize = 120,
        };

        var head = this.BuildNode(first);
        head.Dock = DockStyle.Fill;
        container.Panel1.Controls.Add(head);

        var tail = this.BuildSplit(split, index + 1);
        tail.Dock = DockStyle.Fill;
        container.Panel2.Controls.Add(tail);

        var total = split.Children.Skip(index).Sum(c => Math.Max(0.05, c.Weight));
        var share = Math.Clamp(Math.Max(0.05, first.Weight) / total, 0.1, 0.9);
        _proportions.Add((container, share));

        // Committed drags flow back into the model so the layout persists across sessions.
        container.SplitterMoved += (_, _) => this.SyncWeights(container, split, index);
        return container;
    }

    private void SyncWeights(SplitContainer container, DockSplit split, int index)
    {
        var extent = container.Orientation == Orientation.Vertical ? container.Width : container.Height;
        if (extent <= 0)
            return;

        var share = Math.Clamp((double)container.SplitterDistance / extent, 0.05, 0.95);
        var tailTotal = split.Children.Skip(index + 1).Sum(c => Math.Max(0.05, c.Weight));
        // Keep the tail's internal ratios and give the head the share the user just dragged to.
        split.Children[index].Weight = tailTotal * share / Math.Max(0.001, 1 - share);

        for (var i = 0; i < _proportions.Count; ++i)
            if (ReferenceEquals(_proportions[i].Split, container))
                _proportions[i] = (container, share);
    }

    /// <summary>Re-applies every splitter's share of its container — the toolkit sizes panels in pixels.</summary>
    public void ApplyProportions()
    {
        foreach (var (split, proportion) in _proportions)
        {
            var extent = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            if (extent > 0)
                split.SplitterDistance = (int)(extent * proportion);
        }

        foreach (var pane in _panes.Values)
            pane.ApplyInitialLayout();
    }

    private Control BuildPane(DockPane pane)
    {
        var tabs = pane.Tabs.OfType<FileTabViewModel>().ToList();
        // No strip while there is nothing to switch between: one pane holding one tab is just a
        // folder view. A second pane brings the strips out so tabs can be moved and closed, and a
        // second tab needs its own header to be reachable at all.
        var bare = tabs.Count == 1 && _layout!.Panes().Count() == 1;

        if (bare)
        {
            var only = this.ViewFor(tabs[0]);
            this.WatchPane(pane, null);
            return only;
        }

        var control = new TabControl { Dock = DockStyle.Fill, ShowCloseButtons = true };
        foreach (var tab in tabs)
        {
            var page = new TabPage(tab.Title);
            var view = this.ViewFor(tab);
            view.Dock = DockStyle.Fill;
            page.Controls.Add(view);
            control.TabPages.Add(page);

            // Follow renames without a reflection binding.
            _cleanup.Add(Ui.Watch(tab, () => page.Text = tab.Title, nameof(FileTabViewModel.Title)));
        }

        var active = pane.ActiveTab as FileTabViewModel;
        control.SelectedIndex = active is null ? 0 : Math.Max(0, tabs.IndexOf(active));

        control.SelectedIndexChanged += (_, _) =>
        {
            if (control.SelectedIndex >= 0 && control.SelectedIndex < tabs.Count)
                _layout?.Activate(tabs[control.SelectedIndex]);
        };
        control.TabClosing += (_, e) =>
        {
            e.Cancel = true; // the model owns removal; the rebuild that follows drops the page
            if (control.SelectedIndex >= 0 && control.SelectedIndex < tabs.Count)
                _layout?.CloseTab(tabs[control.SelectedIndex]);
        };

        this.WatchPane(pane, control);
        return control;
    }

    /// <summary>Keeps a pane's strip in step with its model: tab list changes rebuild, activation selects.</summary>
    private void WatchPane(DockPane pane, TabControl? control)
    {
        _cleanup.Add(Ui.ObserveList(pane.Tabs, this.QueueRebuild));
        _cleanup.Add(Ui.Watch(pane, () =>
        {
            if (control is null || pane.ActiveTab is not FileTabViewModel active)
                return;
            var index = pane.Tabs.IndexOf(active);
            if (index >= 0 && index < control.TabPages.Count && control.SelectedIndex != index)
                control.SelectedIndex = index;
        }, nameof(DockPane.ActiveTab)));
    }

    private PaneView ViewFor(FileTabViewModel tab)
    {
        if (_panes.TryGetValue(tab, out var existing))
        {
            // A rebuild re-parents the cached view; the previous parent still lists it until removed.
            existing.Parent?.Controls.Remove(existing);
            return existing;
        }

        var view = new PaneView(_vm, tab) { RowHeight = _rowHeight };
        _panes[tab] = view;
        return view;
    }
}
