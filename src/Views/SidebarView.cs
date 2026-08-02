using System.Drawing;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// One pane's navigation sidebar (PRD §6.2/§6.10): the reorderable sections of favorites, drives,
/// partitions and devices, plus the lazy folder tree. Rows are drive tiles — a caption, a usage bar
/// and a free-space line — collapsed to the caption alone where there is no capacity to show.
/// </summary>
public sealed class SidebarView : Panel
{
    private const int HeaderHeight = 20;
    private const int RowHeight = 30;
    // A compact tile stacks its caption straight over the usage bar, which is the dense drive row
    // the previous UI had; the free-space line goes underneath in the secondary caption.
    private const int CapacityRowHeight = 44;
    private const int TreeHeight = 260;

    private const int TwistyWidth = 18;

    /// <summary>How far each level of folder nesting is inset.</summary>
    private const int NestStep = 12;

    private readonly MainWindowViewModel _shell;
    private readonly FileTabViewModel _tab;
    private readonly List<Action> _cleanup = [];
    private readonly List<Action> _rowCleanup = [];

    /// <summary>
    /// The folder node standing behind each navigable row, kept across rebuilds so a branch someone
    /// opened stays open when the sidebar is rebuilt for an unrelated reason (a volume appearing, a
    /// favourite being pinned).
    /// </summary>
    private readonly Dictionary<string, FolderNodeViewModel> _nodes = new(StringComparer.Ordinal);

    /// <summary>Branches whose child collection this pane is already listening to.</summary>
    private readonly HashSet<FolderNodeViewModel> _watched = [];

    public SidebarView(MainWindowViewModel shell, FileTabViewModel tab)
    {
        _shell = shell;
        _tab = tab;
        this.AutoScroll = true;
        this.BorderStyle = BorderStyle.FixedSingle;

        _cleanup.Add(Ui.WatchList(_shell.Sections, this.Rebuild));
    }

    public void Detach()
    {
        foreach (var undo in _cleanup.Concat(_rowCleanup))
            undo();
        _cleanup.Clear();
        _rowCleanup.Clear();
        _watched.Clear();
    }

    private void Rebuild()
    {
        foreach (var undo in _rowCleanup)
            undo();
        _rowCleanup.Clear();
        this.Controls.Clear();

        // Every row docks to the top so it spans the sidebar whatever width the splitter gives it.
        // Docked siblings claim their edge in reverse order, so the list is built back to front.
        var rows = new List<Control>();

        for (var index = 0; index < _shell.Sections.Count; ++index)
        {
            var section = _shell.Sections[index];

            // The folder tree is no longer a box of its own below the drives: every navigable row in
            // the pane opens into its own folders, which is the one thing the separate tree did that
            // the pane could not. A section holding nothing but that tree has nothing left to show.
            if (section.IsTree)
                continue;

            rows.Add(this.BuildHeader(section, index));

            foreach (var item in section.Items)
            {
                rows.Add(this.BuildRow(item));
                if (this.NodeFor(item) is { IsExpanded: true } node)
                    this.AddChildRows(rows, node, depth: 1);
            }
        }

        rows.Reverse();
        foreach (var row in rows)
            this.Controls.Add(row);
    }

    /// <summary>Makes a row span the sidebar at the given height.</summary>
    private static T Place<T>(T control, int height)
        where T : Control
    {
        control.Dock = DockStyle.Top;
        control.Bounds = new Rectangle(0, 0, 0, height);
        return control;
    }

    // ---- section header ----

    private Control BuildHeader(SidebarSectionViewModel section, int index)
    {
        var header = Place(new IconLabel { Text = section.Title, ForeColor = Color.Gray }, HeaderHeight);

        // Sections used to be reordered by dragging their headers; the toolkit's owner-drawn rows
        // have no drag affordance of their own, so the same move lives on the header's menu.
        var menu = new ContextMenuStrip();
        if (index > 0)
        {
            var previous = _shell.Sections[index - 1].Id;
            menu.Items.Add(MenuAction("Move &up", () => _shell.MoveSidebarSection(section.Id, previous)));
        }

        if (index < _shell.Sections.Count - 1)
        {
            var next = _shell.Sections[index + 1].Id;
            menu.Items.Add(MenuAction("Move &down", () => _shell.MoveSidebarSection(next, section.Id)));
        }

        if (menu.Items.Count > 0)
            header.ContextMenuStrip = menu;

        return header;
    }

    // ---- the fused folder tree ----

    /// <summary>
    /// The folder node behind a navigable row, or null for a row with nowhere to descend into.
    /// </summary>
    /// <remarks>
    /// Kept in a map rather than on the item so it survives the sidebar being rebuilt: the item view
    /// models are recreated whenever a volume appears or a favourite is pinned, and an open branch
    /// closing itself because something unrelated changed would be its own bug.
    /// </remarks>
    private FolderNodeViewModel? NodeFor(SidebarItemViewModel item)
    {
        if (!item.IsNavigable || item.NeedsMounting || item.Path.Length == 0)
            return null;

        if (_nodes.TryGetValue(item.Path, out var existing))
            return existing;

        var node = new FolderNodeViewModel(item.Name, item.Path);
        _nodes[item.Path] = node;
        return node;
    }

    /// <summary>Emits a row for every child of an open branch, and for the branches open inside it.</summary>
    private void AddChildRows(List<Control> rows, FolderNodeViewModel parent, int depth)
    {
        foreach (var child in parent.Children)
        {
            // The placeholder that makes the twisty appear before anything has been read.
            if (child.Path.Length == 0)
                continue;

            rows.Add(this.BuildFolderRow(child, depth));
            if (child.IsExpanded)
                this.AddChildRows(rows, child, depth + 1);
        }
    }

    /// <summary>One folder inside an opened branch: a twisty, an icon and a name, inset by its depth.</summary>
    private Control BuildFolderRow(FolderNodeViewModel node, int depth)
    {
        var label = new IconLabel { Text = node.Name, Image = Icons.FolderIcon, Dock = DockStyle.Fill };
        label.Click += (_, _) => _ = _tab.NavigateToAsync(node.Path);
        FileDrop.Accept(label, _shell, () => node.Path);

        return this.WrapWithTwisty(label, node, depth, RowHeight);
    }

    /// <summary>
    /// Puts a disclosure triangle to the left of a row and insets it for its depth.
    /// </summary>
    /// <remarks>
    /// A separate control rather than a glyph drawn into the row's own caption, so the two hit areas
    /// are the two controls: pressing the triangle opens the branch and pressing anything else goes
    /// there. Sharing one surface would mean hit-testing a caption whose text the row is free to
    /// change.
    /// </remarks>
    private Control WrapWithTwisty(Control content, FolderNodeViewModel? node, int depth, int height)
    {
        var row = Place(new Panel(), height);

        // Docked siblings claim their edge in reverse order, so the content is added before the
        // furniture that has to sit beside it.
        row.Controls.Add(content);

        if (node is not null)
        {
            var twisty = new IconLabel
            {
                Text = node.IsExpanded ? "\u25be" : "\u25b8",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                Dock = DockStyle.Left,
                Bounds = new Rectangle(0, 0, TwistyWidth, height),
            };
            twisty.Click += (_, _) => this.Toggle(node);
            row.Controls.Add(twisty);
        }

        if (depth > 0)
            row.Controls.Add(new Panel { Dock = DockStyle.Left, Bounds = new Rectangle(0, 0, depth * NestStep, height) });

        return row;
    }

    /// <summary>Opens or closes a branch and redraws the pane around it.</summary>
    private void Toggle(FolderNodeViewModel node)
    {
        node.IsExpanded = !node.IsExpanded;

        // Children arrive asynchronously the first time a branch opens, so the rebuild that shows
        // them has to be driven by the collection. The subscription belongs to the pane and not to
        // the rows: _rowCleanup is emptied by every rebuild, and a watcher filed there would be torn
        // down by the very rebuild that follows this line — the branch would open onto nothing and
        // stay that way.
        if (_watched.Add(node))
            _cleanup.Add(Ui.WatchList(node.Children, this.Rebuild));

        this.Rebuild();
    }

    // ---- navigable rows ----

    private Control BuildRow(SidebarItemViewModel item)
    {
        // A partition sits under its disk; the tile paints its own content box, so the nesting is
        // shown by leading space in the caption rather than by insetting the control.
        var indent = item.Indent > 0 ? "    " : string.Empty;
        var tile = Place(
            new ProgressTile
            {
                Text = indent + item.Name,
                Image = Icons.For(item.Kind),
                Clickable = item.IsNavigable,
            },
            item.HasCapacity ? CapacityRowHeight : RowHeight);

        if (item.HasCapacity)
        {
            tile.Compact = true;
            // The bar is drawn in percent so the byte counts never overflow an int.
            tile.Maximum = 100;
            tile.Value = (int)Math.Round(item.UsedFraction * 100);
            tile.WarningThreshold = 90;
            tile.SecondaryText = item.FreeSpaceDisplay;
        }
        else if (item.NeedsMounting)
        {
            // It has a size but nowhere to browse yet, so it says what it is rather than showing a
            // free-space bar it cannot fill.
            tile.Text += "  (not mounted)";
        }
        else if (item.HasFileSystem)
        {
            tile.Text += $"  ({item.FileSystem})";
        }

        if (!item.IsNavigable)
        {
            tile.ForeColor = Color.Gray;
            return tile; // a disk grouping label: nothing to open, nothing to descend into
        }

        tile.Click += (_, _) => _tab.OpenSidebarItemCommand.Execute(item);
        tile.ContextMenuStrip = BuildRowMenu(item);

        // A favorite, drive or device is a folder like any other, so it takes a drop too.
        if (item.Path.Length > 0)
            FileDrop.Accept(tile, _shell, () => item.Path);

        // …and, being a folder, it opens into its own folders right here rather than in a tree of its
        // own further down the pane.
        tile.Dock = DockStyle.Fill;
        return this.WrapWithTwisty(tile, this.NodeFor(item), depth: 0, item.HasCapacity ? CapacityRowHeight : RowHeight);
    }

    private static ContextMenuStrip BuildRowMenu(SidebarItemViewModel item)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Bound("Open", item.OpenCommand, item));
        menu.Items.Add(Bound("Open in New Tab", item.OpenInNewTabCommand, item));
        menu.Items.Add(Bound("Open in New Pane", item.OpenInNewPaneCommand, item));

        if (item.HasActions)
            menu.Items.Add(new ToolStripSeparator());
        if (item.NeedsMounting)
            menu.Items.Add(Bound("Mount", item.OpenCommand, item));
        if (item.IsEjectable)
            menu.Items.Add(Bound("Eject / Unmount", item.EjectCommand, item));
        if (item.CanFormat)
            menu.Items.Add(Bound("Format / create filesystem…", item.FormatCommand, item));
        if (item.CanUnpin)
            menu.Items.Add(Bound("Unpin from favorites", item.UnpinCommand, item));

        return menu;
    }

    // ---- item helpers ----

    private static ToolStripMenuItem MenuAction(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripMenuItem Bound(string text, System.Windows.Input.ICommand? command, object parameter)
    {
        var item = new ToolStripMenuItem(text);
        if (command is null)
            item.Enabled = false;
        else
            item.Click += (_, _) => command.Execute(parameter);
        return item;
    }
}
