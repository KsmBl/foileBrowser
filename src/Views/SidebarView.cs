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

    private readonly MainWindowViewModel _shell;
    private readonly FileTabViewModel _tab;
    private readonly List<Action> _cleanup = [];
    private readonly List<Action> _rowCleanup = [];

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
            rows.Add(this.BuildHeader(section, index));

            if (section.IsTree)
            {
                rows.Add(this.BuildTree());
                continue;
            }

            foreach (var item in section.Items)
                rows.Add(this.BuildRow(item));
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
            return tile;
        }

        tile.Click += (_, _) => _tab.OpenSidebarItemCommand.Execute(item);
        tile.ContextMenuStrip = BuildRowMenu(item);

        // A favorite, drive or device is a folder like any other, so it takes a drop too.
        if (item.Path.Length > 0)
            FileDrop.Accept(tile, _shell, () => item.Path);

        return tile;
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

    // ---- folder tree ----

    private Control BuildTree()
    {
        var tree = Place(new TreeView { ShowRootLines = true }, TreeHeight);

        _rowCleanup.Add(Ui.WatchList(_shell.TreeRoots, () =>
        {
            tree.Nodes.Clear();
            foreach (var root in _shell.TreeRoots)
                tree.Nodes.Add(this.BuildNode(root));
        }));

        // Expanding asks the model to enumerate that branch; the children arrive asynchronously and
        // the collection watcher below swaps the placeholder out for them.
        tree.BeforeExpand += (_, e) =>
        {
            if (e.Node.Tag is FolderNodeViewModel node)
                node.IsExpanded = true;
        };
        tree.AfterSelect += (_, e) =>
        {
            if (e.Node.Tag is FolderNodeViewModel { Path.Length: > 0 } node)
                _ = _tab.NavigateToAsync(node.Path);
        };

        return tree;
    }

    private TreeNode BuildNode(FolderNodeViewModel model)
    {
        var node = new TreeNode(model.Name) { Tag = model };

        _rowCleanup.Add(Ui.WatchList(model.Children, () =>
        {
            node.Nodes.Clear();
            foreach (var child in model.Children)
                node.Nodes.Add(this.BuildNode(child));
        }));

        return node;
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
