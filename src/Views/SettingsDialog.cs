using System.Drawing;
using FoileBrowser.Models;
using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// The tabbed settings dialog (PRD §6.6/§6.8/§6.10): appearance, the operations toolbar, the
/// sidebar, disk formatting and the rebindable hotkey editor. Edits stay local until Save.
/// </summary>
public sealed class SettingsDialog : Form
{
    private readonly AppSettings _settings;
    private readonly List<KeybindRow> _keybinds = [];
    private readonly List<ToolbarOption> _toolbarOptions = [];
    private readonly List<ToolbarOption> _filesystemOptions = [];

    private readonly NumericUpDown _fontSize = new() { Bounds = new(150, 16, 90, 26), Minimum = 8, Maximum = 32 };
    private readonly NumericUpDown _rowHeight = new() { Bounds = new(150, 50, 90, 26), Minimum = 16, Maximum = 64 };
    private readonly CheckBox _searchBar = new() { Text = "Show the search bar by default", Bounds = new(16, 84, 340, 24) };
    private readonly TextBox _terminal = new() { Bounds = new(150, 118, 220, 26) };
    private readonly ComboBox _terminalPicker = new() { Bounds = new(376, 118, 130, 26), PlaceholderText = "detected…" };
    private readonly ComboBox _handoff = new() { Bounds = new(150, 152, 220, 26) };

    private readonly CheckedListBox _toolbarList = new() { Bounds = new(16, 16, 500, 380), CheckOnClick = true };

    private readonly CheckBox _showFavorites = new() { Text = "Favorites", Bounds = new(16, 16, 200, 24) };
    private readonly CheckBox _showDrives = new() { Text = "Drives", Bounds = new(16, 44, 200, 24) };
    private readonly CheckBox _showDevices = new() { Text = "Devices", Bounds = new(16, 72, 200, 24) };
    private readonly CheckBox _showTree = new() { Text = "Folder tree", Bounds = new(16, 100, 200, 24) };
    private readonly ComboBox _treeRoot = new() { Bounds = new(150, 134, 220, 26) };
    private readonly CheckBox _restoreDefaults = new() { Bounds = new(16, 170, 480, 24), Visible = false };

    private readonly CheckBox _enableFormat = new()
    {
        Text = "Allow creating filesystems on block devices (destructive)",
        Bounds = new(16, 16, 480, 24),
    };

    private readonly CheckedListBox _filesystemList = new() { Bounds = new(16, 74, 500, 300), CheckOnClick = true };
    private readonly Label _noMkfs = new() { Bounds = new(16, 46, 500, 22), ForeColor = Color.Gray };

    private readonly KeyCaptureListBox _keybindList = new() { Bounds = new(16, 16, 500, 320) };
    private readonly Button _capture = new() { Text = "Rebind…", Bounds = new(16, 346, 100, 28) };
    private readonly Button _resetOne = new() { Text = "Default", Bounds = new(124, 346, 90, 28) };
    private readonly Button _clearOne = new() { Text = "Unbind", Bounds = new(222, 346, 90, 28) };
    private readonly Button _resetAll = new() { Text = "Reset all", Bounds = new(320, 346, 100, 28) };

    private readonly Label _status = new() { Bounds = new(16, 470, 380, 22), ForeColor = Color.FromArgb(0xE5, 0x48, 0x4D) };

    private KeybindRow? _capturing;

    public SettingsDialog(
        AppSettings settings,
        IReadOnlyList<CommandItem> rebindable,
        IReadOnlyList<FilesystemType> availableFilesystems)
    {
        _settings = settings;

        this.Text = "Settings";
        this.Bounds = new(0, 0, 560, 540);
        this.StartPosition = FormStartPosition.CenterParent;

        Ui.Outline(this);

        var tabs = new TabControl { Bounds = new(8, 8, 536, 450) };
        tabs.TabPages.AddRange(
            this.BuildAppearanceTab(),
            this.BuildToolbarTab(),
            this.BuildSidebarTab(),
            this.BuildDisksTab(availableFilesystems),
            this.BuildKeybindTab(rebindable));

        var save = new Button { Text = "Save", Bounds = new(454, 466, 90, 30) };
        var cancel = new Button { Text = "Cancel", Bounds = new(356, 466, 90, 30) };
        save.Click += this.OnSave;

        this.Controls.AddRange(tabs, _status, save, cancel);
        this.AcceptButton = save;
        this.CancelButton = cancel;

        this.LoadSettings();
    }

    // ---- tabs ----

    private TabPage BuildAppearanceTab()
    {
        var page = new TabPage("Appearance");
        foreach (var name in new[] { "Home and drives", "Root", "Current folder" })
            _treeRoot.Items.Add(name);

        foreach (var name in new[] { "a tab in this window", "a pane beside the current one", "its own window" })
            _handoff.Items.Add(name);

        foreach (var terminal in ShellService.DetectTerminals())
            _terminalPicker.Items.Add(terminal);
        _terminalPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_terminalPicker.SelectedItem is string picked)
                _terminal.Text = picked;
        };

        page.Controls.AddRange(
            new Label { Text = "Font size", Bounds = new(16, 18, 120, 22) },
            _fontSize,
            new Label { Text = "Row height", Bounds = new(16, 52, 120, 22) },
            _rowHeight,
            _searchBar,
            new Label { Text = "Terminal command", Bounds = new(16, 120, 130, 22) },
            _terminal,
            _terminalPicker,
            new Label { Text = "Open a folder in", Bounds = new(16, 154, 130, 22) },
            _handoff,
            new Label
            {
                Text = "…when another program or a second launch asks this one to open one.",
                Bounds = new(16, 182, 500, 22),
                ForeColor = Color.Gray,
            },
            new Label
            {
                Text = "Colours and styling come from the desktop, so there is no theme to choose here.",
                Bounds = new(16, 210, 500, 22),
                ForeColor = Color.Gray,
            });

        return page;
    }

    private TabPage BuildToolbarTab()
    {
        var page = new TabPage("Toolbar");
        page.Controls.Add(_toolbarList);
        _toolbarList.DisplaySelector = static o => ((ToolbarOption)o!).Label;
        return page;
    }

    private TabPage BuildSidebarTab()
    {
        var page = new TabPage("Sidebar");
        page.Controls.AddRange(
            _showFavorites, _showDrives, _showDevices, _showTree,
            new Label { Text = "Tree root", Bounds = new(16, 136, 120, 22) },
            _treeRoot,
            _restoreDefaults);
        return page;
    }

    private TabPage BuildDisksTab(IReadOnlyList<FilesystemType> available)
    {
        var page = new TabPage("Disks");
        _filesystemList.DisplaySelector = static o => ((ToolbarOption)o!).Label;

        foreach (var filesystem in available)
            _filesystemOptions.Add(new ToolbarOption(filesystem.Id, filesystem.Display, enabled: true));

        if (_filesystemOptions.Count == 0)
        {
            _noMkfs.Text = "No mkfs tools are installed, so no filesystem can be created.";
            _filesystemList.Visible = false;
        }

        page.Controls.AddRange(_enableFormat, _noMkfs, _filesystemList);
        return page;
    }

    private TabPage BuildKeybindTab(IReadOnlyList<CommandItem> rebindable)
    {
        var page = new TabPage("Hotkeys");
        _keybindList.DisplaySelector = static o => Describe((KeybindRow)o!);

        foreach (var command in rebindable)
            _keybinds.Add(new KeybindRow(command.Id, command.Title, command.DefaultGesture, command.Gesture));

        _keybindList.ChordCaptured += this.OnChordCaptured;
        _capture.Click += (_, _) => this.BeginCapture();
        _resetOne.Click += (_, _) => this.EditSelected(row => row.Gesture = row.DefaultGesture);
        _clearOne.Click += (_, _) => this.EditSelected(row => row.Gesture = null);
        _resetAll.Click += (_, _) =>
        {
            foreach (var row in _keybinds)
                row.Gesture = row.DefaultGesture;
            this.RecomputeConflicts();
            this.RefreshKeybinds();
        };

        page.Controls.AddRange(
            _keybindList, _capture, _resetOne, _clearOne, _resetAll,
            new Label
            {
                Text = "Pick a command, press Rebind…, then press the chord. Escape cancels the capture.",
                Bounds = new(16, 380, 500, 22),
                ForeColor = Color.Gray,
            });

        this.RecomputeConflicts();
        this.RefreshKeybinds();
        return page;
    }

    private static string Describe(KeybindRow row) =>
        $"{row.Title}   —   {row.Display}{(row.HasConflict ? $"   (!) {row.Conflict}" : string.Empty)}";

    // ---- hotkey capture (PRD §6.6) ----

    private void BeginCapture()
    {
        if (_keybindList.SelectedItem is not KeybindRow row)
            return;

        // Toggle: a second press cancels; arming one cancels any other in-flight capture.
        if (_capturing is not null)
            _capturing.IsCapturing = false;

        _capturing = ReferenceEquals(_capturing, row) ? null : row;
        _keybindList.Capturing = _capturing is not null;
        if (_capturing is not null)
        {
            _capturing.IsCapturing = true;
            _keybindList.Focus();
        }

        this.RefreshKeybinds();
    }

    /// <summary>Binds the captured chord, unless the user pressed Escape to abandon the capture.</summary>
    private void OnChordCaptured(object? sender, Keys chord)
    {
        if (_capturing is null)
            return;

        if ((chord & Keys.KeyCode) != Keys.Escape && Gestures.Format(chord) is { } gesture)
            _capturing.Gesture = gesture;

        _capturing.IsCapturing = false;
        _capturing = null;
        this.RecomputeConflicts();
        this.RefreshKeybinds();
    }

    private void EditSelected(Action<KeybindRow> edit)
    {
        if (_keybindList.SelectedItem is not KeybindRow row)
            return;
        edit(row);
        this.RecomputeConflicts();
        this.RefreshKeybinds();
    }

    private void RefreshKeybinds()
    {
        var index = _keybindList.SelectedIndex;
        _keybindList.Items.Clear();
        foreach (var row in _keybinds)
            _keybindList.Items.Add(row);
        if (index >= 0 && index < _keybinds.Count)
            _keybindList.SelectedIndex = index;
    }

    /// <summary>Flags any two rows sharing a gesture so the user resolves them before saving.</summary>
    private void RecomputeConflicts()
    {
        foreach (var row in _keybinds)
        {
            var gesture = row.Gesture?.Trim();
            row.Conflict = string.IsNullOrEmpty(gesture)
                ? null
                : _keybinds.FirstOrDefault(other => !ReferenceEquals(other, row)
                    && string.Equals(other.Gesture?.Trim(), gesture, StringComparison.OrdinalIgnoreCase)) is { } clash
                    ? $"conflicts with “{clash.Title}”"
                    : null;
        }
    }

    // ---- load / save ----

    private void LoadSettings()
    {
        _fontSize.Value = (decimal)_settings.FontSize;
        _rowHeight.Value = (decimal)_settings.RowHeight;
        _searchBar.Checked = _settings.SearchBarVisible;
        _terminal.Text = _settings.TerminalCommand;
        _handoff.SelectedIndex = _settings.OpenHandoffIn switch { "Pane" => 1, "Window" => 2, _ => 0 };

        foreach (var (id, label) in ToolbarButtons.All)
            _toolbarOptions.Add(new ToolbarOption(id, label, enabled: !_settings.HiddenToolbarButtons.Contains(id)));
        for (var i = 0; i < _toolbarOptions.Count; ++i)
        {
            _toolbarList.Items.Add(_toolbarOptions[i]);
            _toolbarList.SetItemChecked(i, _toolbarOptions[i].IsEnabled);
        }

        _showFavorites.Checked = _settings.SidebarShowFavorites;
        _showDrives.Checked = _settings.SidebarShowDrives;
        _showDevices.Checked = _settings.SidebarShowDevices;
        _showTree.Checked = _settings.SidebarShowTree;
        _treeRoot.SelectedIndex = _settings.TreeRoot switch { "Root" => 1, "Current" => 2, _ => 0 };

        if (_settings.HiddenDefaultFavorites.Count > 0)
        {
            _restoreDefaults.Visible = true;
            _restoreDefaults.Text =
                $"Restore {_settings.HiddenDefaultFavorites.Count} removed built-in favorite(s) on save";
        }

        _enableFormat.Checked = _settings.EnableDiskFormatting;
        for (var i = 0; i < _filesystemOptions.Count; ++i)
        {
            var offered = _settings.FormatFilesystems.Count == 0
                || _settings.FormatFilesystems.Contains(_filesystemOptions[i].Id);
            _filesystemOptions[i].IsEnabled = offered;
            _filesystemList.Items.Add(_filesystemOptions[i]);
            _filesystemList.SetItemChecked(i, offered);
        }
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (_keybinds.Any(row => row.HasConflict))
        {
            _status.Text = "Resolve keybind conflicts first.";
            return;
        }

        _settings.FontSize = (double)_fontSize.Value;
        _settings.RowHeight = (double)_rowHeight.Value;

        _settings.HiddenToolbarButtons = _toolbarOptions
            .Where((_, index) => !_toolbarList.GetItemChecked(index))
            .Select(option => option.Id)
            .ToList();
        _settings.SearchBarVisible = _searchBar.Checked;
        _settings.TerminalCommand = _terminal.Text.Trim();
        _settings.OpenHandoffIn = _handoff.SelectedIndex switch { 1 => "Pane", 2 => "Window", _ => "Tab" };

        _settings.SidebarShowFavorites = _showFavorites.Checked;
        _settings.SidebarShowDrives = _showDrives.Checked;
        _settings.SidebarShowDevices = _showDevices.Checked;
        _settings.SidebarShowTree = _showTree.Checked;
        _settings.TreeRoot = _treeRoot.SelectedIndex switch { 1 => "Root", 2 => "Current", _ => "HomeAndDrives" };
        if (_restoreDefaults.Visible && _restoreDefaults.Checked)
            _settings.HiddenDefaultFavorites.Clear();

        _settings.EnableDiskFormatting = _enableFormat.Checked;
        // Store the offered set only when it is a real subset; all-selected persists as "offer everything".
        var chosen = _filesystemOptions
            .Where((_, index) => _filesystemList.GetItemChecked(index))
            .Select(option => option.Id)
            .ToList();
        _settings.FormatFilesystems = chosen.Count == _filesystemOptions.Count ? [] : chosen;

        // Persist only overrides: a gesture equal to the default is dropped (so it tracks default
        // changes); an empty gesture is stored explicitly to mean "unbound".
        foreach (var row in _keybinds)
        {
            var gesture = row.Gesture?.Trim() ?? string.Empty;
            var fallback = row.DefaultGesture?.Trim() ?? string.Empty;
            if (string.Equals(gesture, fallback, StringComparison.OrdinalIgnoreCase))
                _settings.Keybinds.Remove(row.Id);
            else
                _settings.Keybinds[row.Id] = gesture;
        }

        this.DialogResult = DialogResult.OK;
    }

    public static Task<bool> RequestAsync(
        Form owner,
        AppSettings settings,
        IReadOnlyList<CommandItem> rebindable,
        IReadOnlyList<FilesystemType> availableFilesystems) =>
        Task.FromResult(new SettingsDialog(settings, rebindable, availableFilesystems).ShowDialog(owner) == DialogResult.OK);
}
