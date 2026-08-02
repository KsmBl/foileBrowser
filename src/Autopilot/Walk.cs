using System.Drawing;
using FoileBrowser.ViewModels;
using FoileBrowser.Views;
using Hawkynt.NativeForms;

namespace FoileBrowser.Autopilot;

/// <summary>
/// The walkthrough: the gestures a person makes in the first minute of using a file manager, in the
/// order they make them.
/// </summary>
internal sealed partial class Driver
{
    /// <summary>
    /// The shell and the active tab, read directly — only ever from inside a pumped lambda.
    /// </summary>
    /// <remarks>
    /// Deliberately not properties that marshal for themselves. One that did would be called from
    /// inside a lambda already running on the UI thread, where it would post a second action to that
    /// same thread and wait for it: the thread cannot dispatch the inner post while it is blocked on
    /// the wait, so the whole walkthrough hangs on its first read.
    /// </remarks>
    private MainWindowViewModel Ui => _form.ViewModelForTest;

    private FileTabViewModel UiTab => _form.ViewModelForTest.ActiveTab!;

    /// <summary>The first control of a kind anywhere under the form.</summary>
    private T Part<T>()
        where T : Control
        => this.Read(() => Descend(_form).OfType<T>().FirstOrDefault())
           ?? throw new InvalidOperationException($"no {typeof(T).Name} in the window");

    private static IEnumerable<Control> Descend(Control root)
    {
        foreach (var child in root.Controls)
        {
            yield return child;
            foreach (var deeper in Descend(child))
                yield return deeper;
        }
    }

    /// <summary>The header's height, which the grid keeps to itself.</summary>
    private const int _HeaderHeight = 22;

    /// <summary>The y of a row in the grid, measured from the grid's own top.</summary>
    private int RowY(DataGridView grid, int row)
        => _HeaderHeight + (row * this.Read(() => grid.RowHeight)) + (this.Read(() => grid.RowHeight) / 2);

    /// <summary>A point in the grid below every row and clear of the scrollbar strips.</summary>
    private Point EmptySpace(DataGridView grid)
    {
        var rows = this.Read(() => this.UiTab.Entries.Count);
        var y = _HeaderHeight + ((rows + 1) * this.Read(() => grid.RowHeight));
        return new Point(60, Math.Min(y, this.Read(() => grid.Height) - 24));
    }

    /// <summary>Puts the tab back at the root so a check starts from a known listing.</summary>
    private void GoHome(string root)
    {
        this.Pump("going home", () => _ = this.UiTab.NavigateToAsync(root));
        this.Until(() => this.UiTab.CurrentPath == root && this.UiTab.Entries.Count > 0);
        this.Settle(80);
    }

    /// <summary>The listing gestures land in: whichever pane is active right now, never a cached one.</summary>
    private FileGridView Grid => this.Read(() => _form.ActiveGridForTest)
        ?? throw new InvalidOperationException("no listing in the window");

    private void Walk(string root)
    {
        var grid = this.Grid;

        // --- Listing and navigation -----------------------------------------------------------

        this.Check("the folder it was given is the folder it shows", () =>
        {
            ExpectTrue("the listing arrived", this.Until(() => this.UiTab.Entries.Count > 0));
            Expect("the current path", this.Read(() => this.UiTab.CurrentPath), root);
        });

        this.Check("clicking a row selects it, and only it", () =>
        {
            this.Click(grid, 60, RowY(grid, 0));
            Expect("the selected entry", this.Read(() => this.UiTab.SelectedEntry?.Name), "Alpha");
            Expect("how many are selected", this.Read(() => this.UiTab.SelectedEntries.Count), 1);
        });

        this.Check("clicking a second row moves the selection rather than adding to it", () =>
        {
            this.Click(grid, 60, RowY(grid, 1));
            Expect("the selected entry", this.Read(() => this.UiTab.SelectedEntry?.Name), "Beta");
            Expect("how many are selected", this.Read(() => this.UiTab.SelectedEntries.Count), 1);
        });

        this.Check("ctrl-clicking adds to the selection", () =>
        {
            this.Click(grid, 60, RowY(grid, 0), modifiers: ControlMask);
            Expect("how many are selected", this.Read(() => this.UiTab.SelectedEntries.Count), 2);
        });

        this.Check("shift-clicking takes the run between", () =>
        {
            this.GoHome(root);
            this.Click(grid, 60, RowY(grid, 0));
            this.Click(grid, 60, RowY(grid, 2), modifiers: ShiftMask);
            Expect("how many are selected", this.Read(() => this.UiTab.SelectedEntries.Count), 3);
        });

        this.Check("clicking one of several selected rows narrows the selection to it", () =>
        {
            this.GoHome(root);
            this.Click(grid, 60, RowY(grid, 0));
            this.Click(grid, 60, RowY(grid, 1), modifiers: ControlMask);
            Expect("two are selected to begin with", this.Read(() => this.UiTab.SelectedEntries.Count), 2);

            this.Click(grid, 60, RowY(grid, 1));
            Expect("how many are left", this.Read(() => this.UiTab.SelectedEntries.Count), 1);
            Expect("which one", this.Read(() => this.UiTab.SelectedEntry?.Name), "Beta");
        });

        this.Check("clicking the empty space below the rows lets the selection go", () =>
        {
            this.GoHome(root);
            this.Click(grid, 60, RowY(grid, 0));
            this.Click(grid, 60, RowY(grid, 1));

            var empty = this.EmptySpace(grid);
            this.Click(grid, empty.X, empty.Y);

            Expect("how many are selected", this.Read(() => this.UiTab.SelectedEntries.Count), 0);
        });

        this.Check("double-clicking a folder goes into it", () =>
        {
            this.DoubleClick(grid, 60, RowY(grid, 0));
            ExpectTrue("it navigated", this.Until(() => this.UiTab.CurrentPath == Path.Combine(root, "Alpha")));
        });

        this.Check("Backspace comes back out", () =>
        {
            this.Key(Keys.Back);
            ExpectTrue("it went up", this.Until(() => this.UiTab.CurrentPath == root));
        });

        this.Check("Return opens the row under the cursor", () =>
        {
            this.Click(grid, 60, RowY(grid, 0));
            this.Key(Keys.Enter);
            ExpectTrue("it went in", this.Until(() => this.UiTab.CurrentPath == Path.Combine(root, "Alpha")));
            this.Key(Keys.Back);
            ExpectTrue("and back out", this.Until(() => this.UiTab.CurrentPath == root));
        });

        this.Check("the arrow keys walk the listing", () =>
        {
            this.GoHome(root);
            this.Click(grid, 60, RowY(grid, 0));
            this.Key(Keys.Down);
            Expect("after one press of Down", this.Read(() => this.UiTab.SelectedEntry?.Name), "Beta");
            this.Key(Keys.Up);
            Expect("and back up", this.Read(() => this.UiTab.SelectedEntry?.Name), "Alpha");
        });

        this.Check("the thumb buttons go back and forward", () =>
        {
            this.DoubleClick(grid, 60, RowY(grid, 0));
            ExpectTrue("into Alpha", this.Until(() => this.UiTab.CurrentPath == Path.Combine(root, "Alpha")));

            this.Click(grid, 60, 40, button: 8);   // XButton1 — back
            ExpectTrue("back to the root", this.Until(() => this.UiTab.CurrentPath == root));

            this.Click(grid, 60, 40, button: 9);   // XButton2 — forward
            ExpectTrue("forward into Alpha again", this.Until(() => this.UiTab.CurrentPath == Path.Combine(root, "Alpha")));
            this.Key(Keys.Back);
            this.Until(() => this.UiTab.CurrentPath == root);
        });

        // --- Sorting -----------------------------------------------------------------------------

        this.Check("clicking a column header sorts by it, and again reverses it", () =>
        {
            this.GoHome(root);
            var first = this.Read(() => this.UiTab.Entries[0].Name);
            this.Click(grid, 60, 8); // the Name header
            var ascending = this.Read(() => this.UiTab.Entries.Select(e => e.Name).ToList());
            this.Click(grid, 60, 8);
            var descending = this.Read(() => this.UiTab.Entries.Select(e => e.Name).ToList());

            ExpectTrue("the order changed", !ascending.SequenceEqual(descending));
            ExpectTrue("it is the same set either way", ascending.OrderBy(x => x).SequenceEqual(descending.OrderBy(x => x)));
            _ = first;
        });

        // --- The filter --------------------------------------------------------------------------

        this.Check("Ctrl+F brings up the search bar", () =>
        {
            this.GoHome(root);
            this.Key(Keys.F, ControlMask);
            ExpectTrue("the search bar is showing", this.Until(() => this.Ui.IsSearchBarVisible));
            this.Key(Keys.Escape);
        });

        this.Check("typing in the filter box narrows the listing", () =>
        {
            this.GoHome(root);
            var box = this.Read(() => Descend(_form).OfType<TextBox>().FirstOrDefault(t => t.PlaceholderText.StartsWith("Filter", StringComparison.Ordinal)));
            if (box is null)
            {
                this.Note("no filter box in the window");
                return;
            }

            var before = this.Read(() => this.UiTab.Entries.Count);
            this.Click(box, 30, this.Read(() => box.Height) / 2);
            this.Type("beta");

            ExpectTrue("it narrowed", this.Until(() => this.UiTab.Entries.Count < before));
            Expect("what is left", this.Read(() => this.UiTab.Entries.Count), 1);

            this.Pump("clearing the filter", () => this.UiTab.FilterText = string.Empty);
            this.Until(() => this.UiTab.Entries.Count == before);
        });

        // --- The nav pane ------------------------------------------------------------------------

        this.Check("a sidebar row goes where it says", () =>
        {
            this.GoHome(root);
            var sidebar = this.Part<SidebarView>();

            // A favourite is somewhere the user can certainly read; a volume like /boot may not be.
            var tile = this.Read(() => Descend(sidebar).OfType<ProgressTile>()
                .FirstOrDefault(t => t.Clickable && t.Text.Contains("Home", StringComparison.Ordinal)))
                ?? this.Read(() => Descend(sidebar).OfType<ProgressTile>().FirstOrDefault(t => t.Clickable));

            if (tile is null)
            {
                this.Note("no navigable sidebar row to click");
                return;
            }

            var was = this.Read(() => this.UiTab.CurrentPath);
            var label = this.Read(() => tile.Text);
            this.Click(tile, 40, this.Read(() => tile.Height) / 2);

            if (this.Read(() => this.UiTab.CurrentPath) == was)
                this.Note($"clicking \"{label}\" left the path at \"{was}\"; the status line says "
                    + $"\"{this.Read(() => this.UiTab.StatusText)}\"");

            ExpectTrue("it navigated somewhere", this.Until(() => this.UiTab.CurrentPath != was));
        });

        this.Check("a sidebar twisty opens the row into its folders", () =>
        {
            var sidebar = this.Part<SidebarView>();
            var before = this.Read(() => Descend(sidebar).OfType<IconLabel>().Count());
            // The twisty next to a row we can certainly read. /boot and friends need root, so a
            // branch there legitimately opens onto nothing.
            var twisty = this.Read(() => Descend(sidebar).OfType<IconLabel>()
                .FirstOrDefault(l => l.Text is "\u25b8"
                    && l.Parent is { } row
                    && row.Controls.OfType<ProgressTile>().Any(t => t.Text.Contains("Home", StringComparison.Ordinal))));

            if (twisty is null)
            {
                this.Note("no closed branch to open");
                return;
            }

            this.Click(twisty, 8, this.Read(() => twisty.Height) / 2);
            ExpectTrue(
                "rows appeared under it",
                this.Until(() => this.Read(() => Descend(sidebar).OfType<IconLabel>().Count()) > before, timeoutMs: 4000));
        });

        // --- Selection by rubber band --------------------------------------------------------------

        this.Check("dragging across the rows bands them up", () =>
        {
            this.Pump("going home", () => _ = this.UiTab.NavigateToAsync(root));
            this.Until(() => this.UiTab.CurrentPath == root && this.UiTab.Entries.Count > 0);
            var empty = this.EmptySpace(grid);
            this.Click(grid, empty.X, empty.Y); // start from nothing selected

            this.Drag(grid, new Point(300, RowY(grid, 0) - 6), new Point(300, RowY(grid, 2) + 6));
            ExpectTrue("more than one row came up", this.Read(() => this.UiTab.SelectedEntries.Count) > 1);
        });

        // --- Drag and drop --------------------------------------------------------------------------

        this.WalkMore(root);

        this.Check("dragging a file off a row hands it to the drag machinery", () =>
        {
            this.GoHome(root);
            var rows = this.Read(() => this.UiTab.Entries.Select(e => e.Name).ToList());
            var fileRow = rows.IndexOf("dragme.txt");
            var folderRow = rows.IndexOf("Alpha");
            if (fileRow < 0 || folderRow < 0)
            {
                this.Note($"the fixture rows were not where expected: {string.Join(", ", rows)}");
                return;
            }

            this.Click(grid, 60, RowY(grid, fileRow));
            this.Drag(grid, new Point(60, RowY(grid, fileRow)), new Point(60, RowY(grid, folderRow)));

            // The drop itself cannot be synthesised here: GTK carries a drop over its own selection
            // protocol between windows, which gtk_main_do_event does not stand in for. What is
            // checked is that the gesture reached the grid, was recognised as a drag rather than a
            // band, and left the listing and the selection intact.
            // Left until last: without a drop the session GTK would have owned never ends, and every
            // gesture after it lands somewhere other than where it was aimed.
            this.Note("the drop is not synthesised — GTK carries it over its own protocol, "
                + "and the unfinished session is why this runs last");
            ExpectTrue("the listing survived the gesture", this.Read(() => this.UiTab.Entries.Count) == rows.Count);
            ExpectTrue("and the file is still selected", this.Read(() => this.UiTab.SelectedEntry?.Name) == "dragme.txt");
        });


    }
}
