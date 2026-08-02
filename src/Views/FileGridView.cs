using System.Drawing;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// The virtualized file list (PRD §6.1). Columns come from the shell's shared
/// <see cref="MainWindowViewModel.Columns"/> collection, so header and rows can never drift apart;
/// the user's drag-resize and drag-reorder are written back into it and persisted.
/// </summary>
public sealed class FileGridView : DataGridView
{
    /// <summary>The leading colour stripe that shows an entry's tag (PRD §6.7).</summary>
    private const int TagColumnWidth = 8;

    /// <summary>The grab zone the grid itself uses for a divider — a click inside it resizes, not sorts.</summary>
    private const int DividerZone = 3;

    /// <summary>How far the pointer travels before a press on a selected row becomes a drag.</summary>
    private const int DragThreshold = 5;

    /// <summary>The window two presses on one row count as a double-click, matching the grid's own.</summary>
    private const long DoubleClickWindow = 500;

    private readonly MainWindowViewModel _shell;
    private readonly FileTabViewModel _tab;
    private readonly List<Action> _cleanup = [];

    /// <summary>Which shared column spec each grid column renders (the toolkit's column has no tag slot).</summary>
    private readonly Dictionary<DataGridViewColumn, ColumnSpec> _specs = [];

    /// <summary>
    /// The range a heated column ranks against, recomputed for each listing. A class rather than a
    /// value so the style selector built once per column keeps seeing the current one — the selector
    /// runs on the paint path, where looking anything up would cost per frame what this costs per
    /// folder.
    /// </summary>
    private sealed class HeatRange
    {
        internal bool On;
        internal double Min;
        internal double Max;
    }

    /// <summary>The live range of every heated column, by column id.</summary>
    private readonly Dictionary<string, HeatRange> _heat = [];

    private readonly TypeAhead _typeAhead = new();

    private bool _suppressSelection;
    private Point _pressed = new(-1, -1);
    private Point _dragFrom = new(-1, -1);
    private object? _lastClicked;
    private long _lastClickedAt;

    public FileGridView(MainWindowViewModel shell, FileTabViewModel tab)
    {
        _shell = shell;
        _tab = tab;

        this.MultiSelect = true;
        this.ShowRowHeaders = false;
        this.ShowGridLines = false;
        this.AllowUserToResizeColumns = true;
        this.AllowUserToOrderColumns = true;
        this.EditMode = DataGridViewEditMode.EditProgrammatically; // the list browses; renaming is a dialog
        this.ReadOnly = true;

        this.ContextMenuStrip = this.BuildContextMenu();

        this.SelectionChanged += this.OnSelectionChanged;
        this.CellDoubleClick += (_, _) => this.ActivateSelected();

        _cleanup.Add(Ui.WatchList(_shell.Columns, this.RebuildColumns));
        _cleanup.Add(Ui.WatchList(_tab.Entries, () =>
        {
            _typeAhead.Reset(); // a new listing is a new search
            this.RebuildRows();
            this.RecomputeHeat(); // a new folder is a new range to rank against
        }));

        _shell.HeatColumnsChanged += this.OnHeatColumnsChanged;
        _cleanup.Add(() => _shell.HeatColumnsChanged -= this.OnHeatColumnsChanged);
        _cleanup.Add(Ui.Watch(_tab, this.SyncSelection, nameof(FileTabViewModel.SelectedEntry)));
    }

    public void Detach()
    {
        foreach (var undo in _cleanup)
            undo();
        _cleanup.Clear();
    }

    // ---- columns ----

    private void RebuildColumns()
    {
        this.Columns.Clear();
        _specs.Clear();

        // A narrow leading stripe carrying the entry's colour tag, blank when it has none.
        this.Columns.Add(new DataGridViewColumn(string.Empty, static _ => null)
        {
            Width = TagColumnWidth,
            MinimumWidth = TagColumnWidth,
            Resizable = DataGridViewTriState.False,
            CellStyleSelector = static o =>
                new DataGridViewCellStyle(backColor: Ui.ParseColor(((FileEntryViewModel)o!).TagColor)),
        });

        foreach (var spec in _shell.Columns)
        {
            var id = spec.Id;
            var heat = this.RangeFor(id);
            var kind = spec.Heat;
            var background = this.Theme.FieldBackground;

            var column = new DataGridViewColumn(spec.Header, o => ((FileEntryViewModel)o!).GetCellText(id))
            {
                // The name column leads with the entry's icon, the way a file list is read.
                ImageSelector = id == "name" ? static o => Icons.For(((FileEntryViewModel)o!).Entry.Kind) : null,
                Width = Math.Max(40, (int)spec.Width),
                MinimumWidth = 40,
                Alignment = spec.RightAligned ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft,
                TooltipSelector = static o => ((FileEntryViewModel)o!).FullPath,
                // Hidden entries stay legible but visibly recede, as the dimmed rows did before.
                // A heated column additionally tints its own cells by where the value ranks (§6.1).
                CellStyleSelector = o =>
                {
                    var entry = (FileEntryViewModel)o!;
                    return new DataGridViewCellStyle(
                        foreColor: entry.IsHidden ? Color.Gray : null,
                        backColor: !heat.On ? null
                            : kind == HeatKind.Numeric
                                ? Heat.Numeric(entry.GetHeatValue(id), heat.Min, heat.Max, background)
                                : Heat.Category(entry.GetCellText(id), background));
                },
            };
            _specs[column] = spec;
            this.Columns.Add(column);
        }

        this.RecomputeHeat();
        this.Invalidate();
    }

    // ---- heat maps (PRD §6.1) ----

    /// <summary>The range object for a column, created once and then kept so the style selector that
    /// captured it keeps seeing the current values.</summary>
    private void OnHeatColumnsChanged(object? sender, EventArgs e) => this.RecomputeHeat();

    private HeatRange RangeFor(string id)
    {
        if (_heat.TryGetValue(id, out var range))
            return range;

        return _heat[id] = new HeatRange();
    }

    /// <summary>
    /// Re-ranks every heated column against the rows now listed. The scale is the folder's own
    /// smallest and largest value, so the colours say how the entries here compare with each other
    /// rather than with some absolute the user never set.
    /// </summary>
    private void RecomputeHeat()
    {
        foreach (var range in _heat.Values)
            range.On = false;

        foreach (var spec in _shell.Columns)
        {
            if (spec.Heat is HeatKind.None || !_shell.IsHeated(spec.Id))
                continue;

            var range = this.RangeFor(spec.Id);
            range.On = true;
            if (spec.Heat is not HeatKind.Numeric)
                continue;

            var min = double.MaxValue;
            var max = double.MinValue;
            foreach (var entry in _tab.Entries)
                if (entry.GetHeatValue(spec.Id) is { } value && !double.IsNaN(value))
                {
                    min = Math.Min(min, value);
                    max = Math.Max(max, value);
                }

            range.Min = min;
            range.Max = max;
        }

        this.Invalidate();
    }

    private void RebuildRows()
    {
        _suppressSelection = true;
        this.Items.Clear();
        foreach (var entry in _tab.Entries)
            this.Items.Add(entry);
        _suppressSelection = false;

        this.SyncSelection();
    }

    // ---- selection ----

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressSelection)
            return;

        var selected = this.SelectedItems.OfType<FileEntryViewModel>().ToList();
        _tab.SetSelection(selected);
        _tab.SelectedEntry = selected.Count > 0 ? selected[0] : null;
    }

    private void SyncSelection()
    {
        if (_tab.SelectedEntry is not { } entry || ReferenceEquals(this.SelectedItem, entry))
            return;

        var index = this.Items.IndexOf(entry);
        if (index < 0)
            return;

        _suppressSelection = true;
        this.SelectedRowIndex = index;
        _suppressSelection = false;
    }

    private void ActivateSelected()
    {
        if (_tab.SelectedEntry is { } selected)
            _tab.OpenCommand.Execute(selected);
    }

    // ---- header interaction: sort clicks alongside the grid's own resize/reorder drags ----

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (Gestures.TryNavigate(e, _tab))
            return;

        _pressed = new Point(e.X, e.Y);
        _dragFrom = new Point(-1, -1);

        // A press on a row that is already selected arms a drag instead of a rubber band — pressing
        // anywhere else still bands, and Ctrl/Shift always mean "change the selection".
        if (e.Button == MouseButtons.Left && !e.Control && !e.Shift
            && e.Y >= this.ColumnHeaderHeight && this.SelectedItems.Any()
            && this.RowAt(e.Y) is { } row && this.SelectedItems.Contains(row))
        {
            // Not calling base is what makes the drag possible, and it is also what would swallow
            // the second half of a double-click: the first click selects the row, so the second one
            // lands here and the grid never sees it. The gesture is recognised here instead.
            var now = Environment.TickCount64;
            if (ReferenceEquals(row, _lastClicked) && now - _lastClickedAt <= DoubleClickWindow)
            {
                _lastClicked = null;
                this.ActivateSelected();
                return;
            }

            _lastClicked = row;
            _lastClickedAt = now;
            _dragFrom = new Point(e.X, e.Y);
            return;
        }

        _lastClicked = this.RowAt(e.Y);
        _lastClickedAt = Environment.TickCount64;
        base.OnMouseDown(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragFrom.X >= 0
            && (Math.Abs(e.X - _dragFrom.X) > DragThreshold || Math.Abs(e.Y - _dragFrom.Y) > DragThreshold))
        {
            var paths = this.SelectedItems.OfType<FileEntryViewModel>().Select(entry => entry.FullPath).ToList();
            _dragFrom = new Point(-1, -1);
            if (paths.Count > 0)
                this.DoDragDrop(new FileDrag(paths, _tab.CurrentPath), DragDropEffects.Copy | DragDropEffects.Move);
            return;
        }

        base.OnMouseMove(e);
    }

    /// <summary>The row item under a y-coordinate, or null past the last row.</summary>
    private object? RowAt(int y)
    {
        var row = this.TopRow + ((y - this.ColumnHeaderHeight) / Math.Max(1, this.RowHeight));
        return row >= 0 && row < this.Items.Count ? this.Items[row] : null;
    }

    /// <summary>
    /// Sorting stays the view-model's job (folders lead in both directions — see
    /// <see cref="EntrySorter"/>), so the columns are not self-sorting and a header click is routed
    /// to it instead. A click that moved, or that landed on a divider, was a resize or a reorder and
    /// the grid has already handled it.
    /// </summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var pressed = _pressed;
        _pressed = new Point(-1, -1);
        _dragFrom = new Point(-1, -1);
        base.OnMouseUp(e);

        this.SyncColumnGeometry();

        if (e.Button != MouseButtons.Left || pressed.X < 0 || !this.ShowColumnHeaders)
            return;
        if (pressed.Y >= this.ColumnHeaderHeight || e.Y >= this.ColumnHeaderHeight)
            return;
        if (Math.Abs(e.X - pressed.X) > DividerZone || Math.Abs(e.Y - pressed.Y) > DividerZone)
            return;
        if (this.ColumnAt(pressed.X, out var onDivider) is not { } column || onDivider)
            return;
        if (!_specs.TryGetValue(column, out var spec))
            return;

        _tab.SortByColumnCommand.Execute(spec);
    }

    /// <summary>The column under an x in header space, and whether that x sits on its right divider.</summary>
    private DataGridViewColumn? ColumnAt(int x, out bool onDivider)
    {
        onDivider = false;
        var cx = -this.HorizontalOffset;

        foreach (var column in this.DisplayOrder())
        {
            var right = cx + column.Width;
            if (Math.Abs(x - right) <= DividerZone)
            {
                onDivider = true;
                return column;
            }

            if (x >= cx && x < right)
                return column;

            cx = right;
        }

        return null;
    }

    /// <summary>The columns in the order they are painted — the grid's own rule, without frozen columns.</summary>
    private IEnumerable<DataGridViewColumn> DisplayOrder() =>
        this.Columns
            .Select((column, index) => (column, index))
            .OrderBy(entry => entry.column.DisplayIndex < 0 ? entry.index : entry.column.DisplayIndex)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.column);

    /// <summary>Writes a drag-resize or drag-reorder back into the shared column model and persists it.</summary>
    private void SyncColumnGeometry()
    {
        var changed = false;

        foreach (var (column, spec) in _specs)
        {
            if ((int)spec.Width == column.Width)
                continue;
            spec.Width = column.Width;
            changed = true;
        }

        var order = this.DisplayOrder()
            .Select(column => _specs.TryGetValue(column, out var spec) ? spec.Id : null)
            .Where(id => id is not null)
            .ToList();
        var current = _shell.Columns.Select(c => c.Id).ToList();

        if (!order.SequenceEqual(current))
        {
            // Re-seat the model one hop at a time; the shell persists and rebuilds our columns.
            for (var i = 0; i < order.Count; ++i)
                if (current.IndexOf(order[i]!) != i)
                {
                    _shell.MoveColumn(order[i]!, current[i]);
                    return;
                }
        }

        if (changed)
            _shell.PersistColumns();
    }

    // ---- keyboard (list-scoped, so these keys never fire while typing in a field) ----

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (Gestures.ForListKey(e.KeyCode, e.Alt))
        {
            case ListAction.Activate: this.ActivateSelected(); break;
            case ListAction.GoUp: _tab.GoUpCommand.Execute(null); break;
            case ListAction.QuickPreview: (this.FindForm() as MainForm)?.ShowQuickPreview(); break;
            case ListAction.Delete: _shell.DeleteSelectedCommand.Execute(null); break;
            case ListAction.Rename: _shell.RenameSelectedCommand.Execute(null); break;
            default:
                base.OnKeyDown(e);
                return;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Type-to-select (PRD §6.6): a typed letter jumps to the next entry starting with it. Only
    /// plain typing — a chord belongs to the menu bar, and Space is the quick-preview key.
    /// </summary>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar) || e.KeyChar == ' ')
        {
            base.OnKeyPress(e);
            return;
        }

        var names = _tab.Entries.Select(entry => entry.Name).ToList();
        var index = _typeAhead.Next(e.KeyChar, names, this.SelectedRowIndex, DateTime.UtcNow);
        if (index < 0)
        {
            base.OnKeyPress(e);
            return;
        }

        this.SelectedRowIndex = index;
        this.EnsureVisible(index);
        e.Handled = true;
    }

    // ---- context menu (PRD §6.3/§6.7/§6.9) ----

    private ContextMenuStrip BuildContextMenu()
    {
        // Type to narrow the actions, Enter to run what is left (PRD §6.6). A searchable menu gives
        // up its mnemonics to the filter, which is the trade this menu is worth making and the
        // reason it is opt-in.
        var menu = new ContextMenuStrip { ShowSearchBox = true };

        var openWith = new ToolStripMenuItem("Open with");
        var tag = new ToolStripMenuItem("Tag");

        menu.Items.AddRange(
            Command("Open", _shell.OpenSelectedExternallyCommand),
            openWith,
            new ToolStripSeparator(),
            Command("Copy to other pane", _shell.CopyToOtherCommand),
            Command("Move to other pane", _shell.MoveToOtherCommand),
            Command("Rename…", _shell.RenameSelectedCommand),
            Command("Delete to trash", _shell.DeleteSelectedCommand),
            Command("Delete permanently (overwrite with zeroes)…", _shell.ShredSelectedCommand),
            new ToolStripSeparator(),
            Command("Copy path", _shell.CopyPathCommand),
            Command("Copy name", _shell.CopyNameCommand),
            new ToolStripSeparator(),
            // Pinning used to reach only the folder you were standing in; this pins the one you are
            // pointing at, which is what makes building up a set of favourites bearable.
            Command("Pin folder to favorites", _shell.PinSelectedCommand),
            Command("Extract archive here", _shell.ExtractHereCommand),
            Command("Identify file", _shell.IdentifyFileCommand),
            new ToolStripSeparator(),
            tag,
            Command("Clear tag", _shell.ClearTagCommand),
            new ToolStripSeparator(),
            Command("Properties", _shell.ShowPropertiesCommand),
            new ToolStripSeparator(),
            this.BuildColumnsMenu(),
            this.BuildHeatMenu());

        foreach (var colour in _shell.TagPalette)
        {
            var hex = colour.Hex;
            var item = new ToolStripMenuItem(colour.Name);
            item.Click += (_, _) => _shell.AssignTagCommand.Execute(hex);
            tag.DropDownItems.Add(item);
        }

        menu.Opening += (_, _) =>
        {
            // Scan for "Open with" candidates only now, not on every selection change (PRD §6.9).
            _ = _shell.RefreshOpenWithAsync().ContinueWith(
                _ => this.FillOpenWith(openWith),
                TaskScheduler.FromCurrentSynchronizationContext());
            this.FillOpenWith(openWith);
        };

        return menu;
    }

    private void FillOpenWith(ToolStripMenuItem parent)
    {
        parent.DropDownItems.Clear();
        foreach (var app in _shell.OpenWithApps)
        {
            var target = app;
            var item = new ToolStripMenuItem(app.Name);
            item.Click += (_, _) => _shell.OpenWithAppCommand.Execute(target);
            parent.DropDownItems.Add(item);
        }

        parent.Enabled = parent.HasDropDownItems;
    }

    /// <summary>The add/remove-columns submenu, ticked for the columns currently shown (PRD §6.1).</summary>
    private ToolStripMenuItem BuildColumnsMenu()
    {
        var columns = new ToolStripMenuItem("Columns");

        foreach (var available in _shell.AvailableColumns)
        {
            var id = available.Id;
            var item = new ToolStripMenuItem(available.Header) { CheckOnClick = true };
            item.Click += (_, _) => _shell.ToggleColumn(id);
            columns.DropDownItems.Add(item);

            _cleanup.Add(Ui.WatchList(_shell.Columns,
                () => item.Checked = _shell.Columns.Any(c => c.Id == id)));
        }

        return columns;
    }

    /// <summary>
    /// The heat-map submenu: any shown column that has something to rank or group by can be tinted
    /// by its own values, and more than one at a time — each carries its own scale in its own cells
    /// (PRD §6.1).
    /// </summary>
    private ToolStripMenuItem BuildHeatMenu()
    {
        var heat = new ToolStripMenuItem("Heat map");

        _cleanup.Add(Ui.WatchList(_shell.Columns, () =>
        {
            heat.DropDownItems.Clear();
            foreach (var spec in _shell.Columns)
            {
                if (spec.Heat is HeatKind.None)
                    continue;

                var id = spec.Id;
                var item = new ToolStripMenuItem(spec.Header)
                {
                    CheckOnClick = true,
                    Checked = _shell.IsHeated(id),
                    ToolTipText = spec.Heat is HeatKind.Numeric
                        ? "Tint by where the value ranks in this folder"
                        : "Give each distinct value its own colour",
                };
                item.Click += (_, _) => _shell.ToggleHeatColumn(id);
                heat.DropDownItems.Add(item);
            }

            heat.Enabled = heat.HasDropDownItems;
        }));

        return heat;
    }

    private static ToolStripMenuItem Command(string text, System.Windows.Input.ICommand command)
    {
        var item = new ToolStripMenuItem(text) { Command = command };
        return item;
    }
}
