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
    /// <summary>Tall enough for a <see cref="Icons.ToolbarSize"/> glyph with breathing room around it.</summary>
    private const int ToolbarHeight = 34;

    /// <summary>How far a new window is offset from the one that opened it.</summary>
    private const int CascadeStep = 36;

    private readonly MainWindowViewModel _vm;
    private readonly MenuStrip _menu = new() { Dock = DockStyle.Top, Bounds = new(0, 0, 0, MenuHeight) };
    private readonly ToolStrip _toolbar = new() { Dock = DockStyle.Top, Bounds = new(0, 0, 0, ToolbarHeight) };
    private readonly PreviewPane _inspector;
    private readonly OperationsView _operations;

    /// <summary>
    /// Hosts the pane tree as the document and the side panels around it (PRD §6.2/§6.5).
    /// </summary>
    /// <remarks>
    /// The panels used to be docked straight onto the form at a width the view chose, which left them
    /// neither resizable nor movable: the preview was 280 pixels wide for ever, wherever the picture
    /// wanted more. Here each is a piece of furniture the user can drag to another edge, tear off into
    /// a window of its own, or auto-hide — and where they are put is remembered.
    /// </remarks>
    // No caption over the file panes: it is the one bar in the window that named something the tabs
    // beneath it already name, and a document area cannot be closed or floated as a group anyway.
    private readonly DockPanel _panels = new() { Dock = DockStyle.Fill, ShowDocumentCaption = false };

    private readonly DockContent _filesContent = new("Files") { PersistId = "files", AllowClose = false };
    private readonly DockContent _previewContent = new("Preview") { PersistId = "preview" };
    private readonly DockContent _operationsContent = new("Operations") { PersistId = "operations" };
    private readonly DockLayoutView _dock;

    /// <summary>Every shell window this process has open, so the last one out can end the loop.</summary>
    private static readonly List<MainForm> Windows = [];

    private readonly bool _primary;

    private CommandPaletteDialog? _paletteDialog;
    private bool _sessionSaved;

    public MainForm(MainWindowViewModel viewModel, bool primary)
    {
        _vm = viewModel;
        _primary = primary;

        // No window ends the loop by itself: the process lives until its last window is gone, so
        // closing any one of several closes only that one.
        this.QuitsOnClose = false;
        Windows.Add(this);

        this.Text = "foileBrowser";
        // Wide enough for two panes, their sidebars and the inspector to all show real content;
        // below about this the file list is squeezed down to its first column.
        this.Bounds = new(0, 0, 1360, 820);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MinimumSize = new Size(720, 420);
        this.ApplyIcon();

        _dock = new DockLayoutView(_vm) { Dock = DockStyle.Fill };
        _inspector = new PreviewPane(_vm.Thumbnails) { Dock = DockStyle.Fill };
        _operations = new OperationsView(_vm.OperationQueue, _vm.Display) { Dock = DockStyle.Fill };

        _filesContent.Controls.Add(_dock);
        _previewContent.Controls.Add(_inspector);
        _operationsContent.Controls.Add(_operations);

        _panels.AddDocument(_filesContent);
        _panels.Add(_previewContent, DockState.Docked, DockEdge.Right);
        _panels.Add(_operationsContent, DockState.Docked, DockEdge.Bottom);

        // Reverse order: the last child added claims its edge first, so the menu ends up outermost
        // and the dock area takes whatever is left (see Control.OnLayout).
        this.Controls.Add(_panels);
        this.Controls.Add(_toolbar);
        this.Controls.Add(_menu);

        this.BuildMenu();
        this.BuildToolbar();

        this.Load += this.OnLoad;
        this.FormClosing += this.OnFormClosing;
        this.FormClosed += this.OnFormClosed;
        // Splitter positions are proportions in the model but pixels in the toolkit, so they are
        // re-derived whenever the window changes size (Form is the only control that reports one).
        this.Resize += (_, _) =>
        {
            _dock.ApplyProportions();
            _inspector.Reframe();
        };

        this.WireViewModel();
    }

    // ---- lifecycle ----

    private async void OnLoad(object? sender, EventArgs e)
    {
        this.ApplySettings();
        await _vm.InitializeAsync();
        this.RestorePanelLayout();
        // The pane tree only exists once the session restored, so its splitters are placed after it.
        _dock.ApplyProportions();

        if (_pendingPath is { } startup && _vm.ActiveTab is { } tab)
        {
            _pendingPath = null;
            await GoTo(tab, startup);
        }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Only the window the loop started on owns the saved session; a second window's tabs are
        // this run's, not the layout to come back to.
        if (_sessionSaved || !_primary)
            return;

        // The session write is asynchronous, so the first close is vetoed, awaited, then repeated.
        e.Cancel = true;
        _vm.Settings.PanelLayout = _panels.SaveLayout();
        await _vm.SaveSessionAsync();
        _sessionSaved = true;
        this.Close();
    }

    private void OnFormClosed(object? sender, EventArgs e)
    {
        Windows.Remove(this);
        if (Windows.Count == 0)
            Application.Exit();
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

        // A name clash asks rather than silently auto-renaming (PRD §6.3); "apply to all" is
        // forgotten between queued operations.
        var conflicts = new ConflictDialog.Prompt(this);
        _vm.OperationQueue.ConflictResolver = conflicts.Resolve;
        _vm.OperationQueue.OperationCompleted += (_, _) => conflicts.Reset();

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

    /// <summary>
    /// Puts the panels back where they were left, if they were ever moved.
    /// </summary>
    /// <remarks>
    /// A layout saved by an older build can name a panel this one no longer has; the resolver answers
    /// null for those and the dock leaves them out rather than refusing the whole layout.
    /// </remarks>
    private void RestorePanelLayout()
    {
        var layout = _vm.Settings.PanelLayout;
        if (string.IsNullOrEmpty(layout))
            return;

        try
        {
            _panels.LoadLayout(layout, id => id switch
            {
                "files" => _filesContent,
                "preview" => _previewContent,
                "operations" => _operationsContent,
                _ => null,
            });
        }
        catch (Exception)
        {
            // A layout that will not parse is not worth failing to start over.
        }
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
        _previewContent.DockState = _vm.IsInspectorOpen ? DockState.Docked : DockState.Hidden;
    }

    private void SyncOperations()
    {
        var count = _vm.OperationQueue.Operations.Count;
        _operations.Sync();
        // The panel comes up when there is something to report and goes away when there is not, but a
        // user who has pulled it somewhere of their own keeps it there.
        if (count > 0)
        {
            if (_operationsContent.DockState == DockState.Hidden)
                _operationsContent.DockState = DockState.Docked;
        }
        else if (_operationsContent.DockState == DockState.Docked)
            _operationsContent.DockState = DockState.Hidden;
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

    /// <summary>The shell behind this window, so the autopilot can read what a gesture did.</summary>
    internal MainWindowViewModel ViewModelForTest => _vm;

    private string? _pendingPath;

    /// <summary>
    /// Opens a folder handed over by another launch (PRD §6.12), where the settings say it should
    /// go: a tab in this window, a pane split beside the current one, or a window of its own. All
    /// three share this process and its services — only the controls are new.
    /// </summary>
    public async void OpenPath(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path))
            return;

        switch (_vm.Settings.OpenHandoffIn)
        {
            case "Window":
                var window = App.CreateWindow();
                window.OpenAtStartup(path);
                // Cascade off this one rather than landing exactly on top of it, which would look
                // like nothing happened.
                window.StartPosition = FormStartPosition.Manual;
                window.Bounds = new Rectangle(
                    this.Left + CascadeStep, this.Top + CascadeStep, this.Width, this.Height);
                window.Show();
                return;

            case "Pane":
                await _vm.AddPaneCommand.ExecuteAsync(null);
                break;

            default:
                await _vm.AddTabCommand.ExecuteAsync(null);
                break;
        }

        if (_vm.ActiveTab is { } tab)
            await GoTo(tab, path);

        // Bring the window forward: the user just asked for this folder from somewhere else.
        if (this.WindowState == FormWindowState.Minimized)
            this.WindowState = FormWindowState.Normal;
        this.Focus();
    }

    /// <summary>
    /// Goes to whatever was asked for: a folder is browsed, an archive is entered as one, and any
    /// other file lands in its parent folder so it can be seen in context.
    /// </summary>
    private static async Task GoTo(FileTabViewModel tab, string path)
    {
        if (Directory.Exists(path))
        {
            await tab.NavigateToAsync(path);
            return;
        }

        var parent = Path.GetDirectoryName(path);
        if (parent is null)
            return;

        await tab.NavigateToAsync(parent);
        if (tab.Entries.FirstOrDefault(e => e.FullPath == path) is not { } entry)
            return;

        // Reveal it, do not launch it. Being handed a file means "show me where this is" — which is
        // what a desktop's "show in file manager" asks for, and what every other file manager does
        // with it. Running OpenCommand here instead handed the file straight to whatever application
        // claims it, so asking to be shown a picture opened an image viewer over the top.
        tab.SelectedEntry = entry;
        tab.SetSelection([entry]);
    }

    /// <summary>Opens the spacebar quick-preview window for the current inspector preview (PRD §6.5).</summary>
    public void ShowQuickPreview()
    {
        if (_vm.Preview is { } preview)
            new QuickPreviewForm(preview, _vm.Thumbnails).ShowDialog(this);
    }
}
