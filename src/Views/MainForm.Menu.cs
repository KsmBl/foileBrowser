using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// The menu bar and the global operations toolbar. Every entry that has a registered command is
/// built from the command registry, so its shortcut stays whatever the user rebound it to
/// (PRD §6.6) — <see cref="RefreshShortcuts"/> re-reads them whenever the bindings change.
/// </summary>
public sealed partial class MainForm
{
    /// <summary>Menu items built from a command id, so their shortcut text follows a rebind.</summary>
    private readonly List<(ToolStripMenuItem Item, string CommandId)> _shortcutItems = [];

    private ToolStripMenuItem? _toolbarToggle;
    private ToolStripMenuItem? _inspectorToggle;

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(
            this.CommandItem("New &Folder", "file.newFolder"),
            this.CommandItem("New F&ile", "file.newFile"),
            new ToolStripSeparator(),
            this.CommandItem("&Rename…", "file.rename"),
            this.CommandItem("&Delete to Trash", "file.delete"),
            this.CommandItem("Delete &Permanently…", "file.shred"),
            this.CommandItem("&Batch Rename…", "file.batchRename"),
            new ToolStripSeparator(),
            this.CommandItem("Copy &Path", "file.copyPath"),
            this.CommandItem("Copy &Name", "file.copyName"),
            new ToolStripSeparator(),
            this.CommandItem("P&roperties", "file.properties"),
            new ToolStripSeparator(),
            Action("E&xit", this.Close));

        var edit = new ToolStripMenuItem("&Edit");
        edit.DropDownItems.AddRange(
            this.CommandItem("&Undo", "edit.undo"),
            this.CommandItem("&Redo", "edit.redo"),
            new ToolStripSeparator(),
            this.CommandItem("Copy to Other Pane", "file.copyToOther"),
            this.CommandItem("Move to Other Pane", "file.moveToOther"),
            new ToolStripSeparator(),
            this.CommandItem("Extract Archive Here", "archive.extract"),
            this.CommandItem("Identify File", "archive.identify"));

        _toolbarToggle = this.CommandItem("&Toolbar", "view.toggleToolbar");
        _inspectorToggle = this.CommandItem("&Inspector", "view.toggleInspector");

        var view = new ToolStripMenuItem("&View");
        view.DropDownItems.AddRange(
            this.CommandItem("New &Tab", "tab.new"),
            this.CommandItem("New &Pane (split)", "view.newPane"),
            this.CommandItem("&Close Tab", "tab.close"),
            new ToolStripSeparator(),
            _toolbarToggle,
            _inspectorToggle,
            this.CommandItem("&Hidden Files", "view.toggleHidden"),
            new ToolStripSeparator(),
            this.CommandItem("Size &Units", "view.sizeUnit"),
            this.CommandItem("Date &Format", "view.dateFormat"),
            new ToolStripSeparator(),
            this.CommandItem("&Command Palette…", "app.commandPalette"),
            this.CommandItem("&Settings…", "app.settings"));

        var go = new ToolStripMenuItem("&Go");
        go.DropDownItems.AddRange(
            this.CommandItem("&Back", "nav.back"),
            this.CommandItem("&Forward", "nav.forward"),
            this.CommandItem("&Up", "nav.up"),
            this.CommandItem("&Refresh", "nav.refresh"),
            new ToolStripSeparator(),
            this.CommandItem("&Edit Path", "nav.editPath"),
            this.CommandItem("Find in Folder", "search.focus"));

        var tools = new ToolStripMenuItem("&Tools");
        tools.DropDownItems.AddRange(
            this.CommandItem("Open &Terminal Here", "os.terminal"),
            this.CommandItem("&Open With…", "os.openWith"),
            this.CommandItem("&Pin Current Folder", "fav.pin"),
            new ToolStripSeparator(),
            this.CommandItem("Clear Tag", "tag.clear"),
            this.CommandItem("Clear Tag Filter", "tag.filterClear"));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Action("&About foileBrowser", () => _vm.ShowAboutCommand.Execute(null)));

        _menu.Items.AddRange(file, edit, view, go, tools, help);

        Ui.Watch(_vm, () =>
        {
            if (_toolbarToggle is not null)
                _toolbarToggle.Checked = _vm.IsToolbarVisible;
            if (_inspectorToggle is not null)
                _inspectorToggle.Checked = _vm.IsInspectorOpen;
        }, nameof(MainWindowViewModel.IsToolbarVisible), nameof(MainWindowViewModel.IsInspectorOpen));
    }

    /// <summary>A menu item driven by a registered command — caption here, action and hotkey from the registry.</summary>
    private ToolStripMenuItem CommandItem(string text, string commandId)
    {
        var item = new ToolStripMenuItem(text);
        if (_vm.Commands.FirstOrDefault(c => c.Id == commandId) is { } command)
        {
            item.Command = command.Command;
            ApplyShortcut(item, command);
            _shortcutItems.Add((item, commandId));
        }
        else
        {
            item.Enabled = false; // a caption with no command behind it would silently do nothing
        }

        return item;
    }

    /// <summary>
    /// The caption for the two buttons that have no icon, because what they show is their current
    /// value ("KiB", "Ago") rather than a picture. Everything else carries a drawn icon; the bar's
    /// right-click menu spells out what each one does, since a strip item has no tooltip of its own.
    /// </summary>
    private static string Caption(ToolbarItemViewModel item) => item.Id switch
    {
        "newFolder" => "New folder",
        "newFile" => "New file",
        "rename" => "Rename",
        "delete" => "Delete",
        "copyToOther" => "Copy to pane",
        "moveToOther" => "Move to pane",
        "copyPath" => "Copy path",
        "copyName" => "Copy name",
        "batchRename" => "Batch rename",
        "terminal" => "Terminal",
        "pin" => "Pin",
        "newTab" => "New tab",
        "inspector" => "Inspector",
        "settings" => "Settings",
        // The size/date buttons already carry a live word ("KiB", "Ago") rather than a picture.
        _ => item.Content,
    };

    private static ToolStripMenuItem Action(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>
    /// Gives a menu item its chord. Only window-wide commands are actually dispatched by the bar:
    /// the list-scoped keys (Delete, F2) stay with the file list so they act on files rather than
    /// firing while the user is typing in a field — but the menu still advertises them.
    /// </summary>
    private static void ApplyShortcut(ToolStripMenuItem item, CommandItem command)
    {
        if (command.Global)
        {
            item.ShortcutKeys = Gestures.Parse(command.Gesture);
            item.ShortcutKeyDisplayString = null;
            return;
        }

        item.ShortcutKeys = Keys.None;
        item.ShortcutKeyDisplayString = command.Gesture;
    }

    /// <summary>Re-reads every menu shortcut after the keybinds loaded or were rebound (PRD §6.6).</summary>
    private void RefreshShortcuts()
    {
        foreach (var (item, id) in _shortcutItems)
            if (_vm.Commands.FirstOrDefault(c => c.Id == id) is { } command)
                ApplyShortcut(item, command);
    }

    // ---- global operations toolbar (PRD §6.3/§6.8) ----

    private void BuildToolbar()
    {
        Ui.WatchList(_vm.ToolbarItems, this.RebuildToolbarItems);
    }

    private void RebuildToolbarItems()
    {
        _toolbar.Items.Clear();

        foreach (var item in _vm.ToolbarItems)
        {
            var icon = Icons.ForToolbar(item.Id);
            var button = new ToolStripButton(icon is null ? Caption(item) : string.Empty)
            {
                Image = icon,
                Command = item.Command,
                Visible = item.IsVisible,
                Tag = item.Id,
            };
            // The size/date buttons relabel themselves live, and Settings can hide any of them.
            Ui.Watch(item, () =>
            {
                if (icon is null)
                    button.Text = Caption(item);
                button.Visible = item.IsVisible;
            }, nameof(ToolbarItemViewModel.Content), nameof(ToolbarItemViewModel.IsVisible));
            _toolbar.Items.Add(button);
        }

        _toolbar.ContextMenuStrip = this.BuildToolbarMenu();
    }

    /// <summary>
    /// The toolbar's own context menu. It carries what a strip button cannot: the descriptive label
    /// each button's icon stands for, and the reorder gesture — the toolkit's strip items are not
    /// controls, so they have neither tooltips nor drag handles of their own (PRD §6.8).
    /// </summary>
    private ContextMenuStrip BuildToolbarMenu()
    {
        var menu = new ContextMenuStrip();

        for (var index = 0; index < _vm.ToolbarItems.Count; ++index)
        {
            var item = _vm.ToolbarItems[index];
            var entry = new ToolStripMenuItem(item.Tooltip);

            var position = index;
            if (position > 0)
            {
                var previous = _vm.ToolbarItems[position - 1].Id;
                entry.DropDownItems.Add(Action("Move &left", () => _vm.MoveToolbarItem(item.Id, previous)));
            }

            if (position < _vm.ToolbarItems.Count - 1)
            {
                var next = _vm.ToolbarItems[position + 1].Id;
                entry.DropDownItems.Add(Action("Move &right", () => _vm.MoveToolbarItem(next, item.Id)));
            }

            menu.Items.Add(entry);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Action("Show/hide buttons…", () => _vm.OpenSettingsCommand.Execute(null)));
        return menu;
    }
}
