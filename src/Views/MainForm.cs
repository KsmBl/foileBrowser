using System.Drawing;
using System.Reflection;
using FoileBrowser.Models;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// The shell window: menu bar, global operations toolbar, the dockable pane area, the inspector and
/// the background-operation strip (PRD §6.1–§6.6). It owns every dialog the view-model asks for and
/// nothing else — all behaviour lives in <see cref="MainWindowViewModel"/>.
/// </summary>
public sealed partial class MainForm : Form
{
    private const int MenuHeight = 26;
    private const int ToolbarHeight = 30;
    private const int InspectorWidth = 280;
    private const int OperationRowHeight = 24;

    private readonly MainWindowViewModel _vm;
    private readonly MenuStrip _menu = new() { Dock = DockStyle.Top, Bounds = new(0, 0, 0, MenuHeight) };
    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, Bounds = new(0, 0, 0, ToolbarHeight) };
    private readonly InspectorView _inspector;
    private readonly OperationsView _operations;
    private readonly DockLayoutView _dock;

    private CommandPaletteDialog? _paletteDialog;
    private bool _sessionSaved;

    public MainForm(MainWindowViewModel viewModel)
    {
        _vm = viewModel;

        this.Text = "foileBrowser";
        // Wide enough for two panes, their sidebars and the inspector to all show real content;
        // below about this the file list is squeezed down to its first column.
        this.Bounds = new(0, 0, 1360, 820);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MinimumSize = new Size(720, 420);
        this.ApplyIcon();

        _dock = new DockLayoutView(_vm) { Dock = DockStyle.Fill };
        _inspector = new InspectorView
        {
            Dock = DockStyle.Right,
            Bounds = new(0, 0, InspectorWidth, 0),
            BorderStyle = BorderStyle.FixedSingle,
        };
        _operations = new OperationsView(_vm.OperationQueue)
        {
            Dock = DockStyle.Bottom,
            Bounds = new(0, 0, 0, 0),
        };

        // Reverse order: the last child added claims its edge first, so the menu ends up outermost
        // and the pane area takes whatever is left (see Control.OnLayout).
        this.Controls.Add(_dock);
        this.Controls.Add(_inspector);
        this.Controls.Add(_operations);
        this.Controls.Add(_toolbar);
        this.Controls.Add(_menu);

        this.BuildMenu();
        this.BuildToolbar();

        this.Load += this.OnLoad;
        this.FormClosing += this.OnFormClosing;
        // Splitter positions are proportions in the model but pixels in the toolkit, so they are
        // re-derived whenever the window changes size (Form is the only control that reports one).
        this.Resize += (_, _) => _dock.ApplyProportions();

        this.WireViewModel();
    }

    // ---- lifecycle ----

    private async void OnLoad(object? sender, EventArgs e)
    {
        this.ApplySettings();
        await _vm.InitializeAsync();
        // The pane tree only exists once the session restored, so its splitters are placed after it.
        _dock.ApplyProportions();

        if (_pendingPath is { } startup && _vm.ActiveTab is { } tab)
        {
            _pendingPath = null;
            await tab.NavigateToAsync(startup);
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // The session write is asynchronous, so the first close is vetoed, awaited, then repeated.
        if (_sessionSaved)
            return;

        e.Cancel = true;
        await _vm.SaveSessionAsync();
        _sessionSaved = true;
        this.Close();
    }

    /// <summary>Loads the embedded PNG through the toolkit's own decoder — no image pipeline needed.</summary>
    private void ApplyIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("foilebrowser.png");
            if (stream is null)
                return;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var decoded = ImageDecoder.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            this.SetIcon(decoded.Width, decoded.Height, decoded.Frames[0].Argb);
        }
        catch (Exception)
        {
            // A missing or unreadable icon is cosmetic; the window opens either way.
        }
    }

    // ---- view-model wiring ----

    private void WireViewModel()
    {
        // The view supplies the prompts, dialogs and clipboard the view-model asks for.
        _vm.NameRequester = current => NameInputDialog.RequestAsync(this, current);
        _vm.BatchRenameRequester = entries => BatchRenameDialog.RequestAsync(this, entries);
        _vm.SettingsRequester = settings =>
            SettingsDialog.RequestAsync(this, settings, _vm.RebindableCommands, _vm.Disk.AvailableFilesystems());
        _vm.FormatRequester = item => FormatDialog.RequestAsync(this, item, _vm.Disk, _vm.AllowedFilesystems());
        _vm.PropertiesRequester = entry => PropertiesDialog.ShowAsync(this, entry, _vm.Sizes, _vm.Applications);
        _vm.ShredConfirmRequester = paths => ShredConfirmDialog.RequestAsync(this, paths);

        _vm.ClipboardCopyRequested += (_, text) =>
        {
            if (!string.IsNullOrEmpty(text))
                Clipboard.SetText(text);
        };
        _vm.ThemeChanged += (_, _) => this.ApplySettings();
        _vm.KeybindsChanged += (_, _) => this.RefreshShortcuts();

        Ui.Watch(_vm, this.SyncInspector, nameof(MainWindowViewModel.IsInspectorOpen), nameof(MainWindowViewModel.Preview));
        Ui.Watch(_vm, () => Ui.SetDockedExtent(_toolbar, _vm.IsToolbarVisible, ToolbarHeight),
            nameof(MainWindowViewModel.IsToolbarVisible));
        Ui.WatchList(_vm.OperationQueue.Operations, this.SyncOperations);
        Ui.Watch(_vm.CommandPalette, this.SyncPalette, nameof(CommandPaletteViewModel.IsOpen));
    }

    /// <summary>Applies the settings the toolkit can honour: text size and row density (PRD §6.8).</summary>
    private void ApplySettings()
    {
        var settings = _vm.Settings;
        if (settings.FontSize > 0)
            this.Font = new Font(this.Font.Family, (float)settings.FontSize);
        _dock.RowHeight = Math.Max(16, (int)settings.RowHeight);
    }

    private void SyncInspector()
    {
        _inspector.Show(_vm.Preview);
        Ui.SetDockedExtent(_inspector, _vm.IsInspectorOpen, InspectorWidth);
    }

    private void SyncOperations()
    {
        var count = _vm.OperationQueue.Operations.Count;
        _operations.Sync();
        Ui.SetDockedExtent(_operations, count > 0, Math.Min(4, count) * OperationRowHeight + 8);
    }

    private void SyncPalette()
    {
        if (!_vm.CommandPalette.IsOpen)
            return;

        // Shown as its own small window: the toolkit has no z-ordered overlay layer inside a form,
        // and a modal keeps the same "type, arrow, Enter" flow the palette had as an overlay.
        _paletteDialog ??= new CommandPaletteDialog(_vm.CommandPalette);
        _paletteDialog.ShowDialog(this);
    }

    /// <summary>The folder a launch asked for, opened once the shell has finished starting up.</summary>
    public void OpenAtStartup(string path) => _pendingPath = path;

    private string? _pendingPath;

    /// <summary>
    /// Opens a folder in this window on behalf of another launch (PRD §6.12). It arrives as a tab
    /// rather than a second window because the message loop is anchored to this form — closing it
    /// would take any sibling window down with it — and a tab shares even more than a window would.
    /// </summary>
    public async void OpenPath(string path)
    {
        if (!Directory.Exists(path))
            return;

        await _vm.AddTabCommand.ExecuteAsync(null);
        if (_vm.ActiveTab is { } tab)
            await tab.NavigateToAsync(path);

        // Bring the window forward: the user just asked for this folder from somewhere else.
        if (this.WindowState == FormWindowState.Minimized)
            this.WindowState = FormWindowState.Normal;
        this.Focus();
    }

    /// <summary>Opens the spacebar quick-preview window for the current inspector preview (PRD §6.5).</summary>
    public void ShowQuickPreview()
    {
        if (_vm.Preview is { } preview)
            new QuickPreviewForm(preview).ShowDialog(this);
    }
}
