using FoileBrowser.Services;
using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// The gallery: the same folder as large thumbnails instead of rows (PRD §6.2), for the folders
/// where what a file looks like matters more than its size and date.
///
/// Thumbnails arrive from a background decoder, so a cell shows its kind icon first and swaps to the
/// picture when it is ready — a folder of a thousand photographs is browsable immediately rather
/// than after it has all been read.
/// </summary>
public sealed class GalleryView : ListView
{
    private readonly MainWindowViewModel _shell;
    private readonly FileTabViewModel _tab;
    private readonly ThumbnailService _thumbnails;
    private readonly List<Action> _cleanup = [];
    private readonly TypeAhead _typeAhead = new();

    private bool _suppressSelection;
    private System.Drawing.Point _dragFrom = new(-1, -1);

    /// <summary>How far the pointer travels before a press on a selected cell becomes a drag.</summary>
    private const int DragThreshold = 5;

    public GalleryView(MainWindowViewModel shell, FileTabViewModel tab, ThumbnailService thumbnails)
    {
        _shell = shell;
        _tab = tab;
        _thumbnails = thumbnails;

        this.View = ListViewView.LargeIcon;
        this.MultiSelect = true;
        // The image list is empty; it is here because it is what gives an icon view its cell size.
        this.LargeImageList = new ImageList(ThumbnailService.Edge);

        this.ItemActivate += (_, _) => this.ActivateSelected();
        this.SelectedIndexChanged += this.OnSelectionChanged;

        _cleanup.Add(Ui.WatchList(_tab.Entries, () =>
        {
            _typeAhead.Reset();
            this.Rebuild();
        }));
        _cleanup.Add(Ui.Watch(_tab, this.SyncSelection, nameof(FileTabViewModel.SelectedEntry)));

        _thumbnails.Ready += this.OnThumbnailReady;
    }

    public void Detach()
    {
        _thumbnails.Ready -= this.OnThumbnailReady;
        foreach (var undo in _cleanup)
            undo();
        _cleanup.Clear();
    }

    private void Rebuild()
    {
        _suppressSelection = true;
        this.Items.Clear();
        foreach (var entry in _tab.Entries)
            this.Items.Add(new ListViewItem(entry.Name) { Tag = entry, Image = this.IconFor(entry) });
        _suppressSelection = false;

        this.SyncSelection();
    }

    /// <summary>A ready thumbnail, or the entry's kind icon while one is being decoded.</summary>
    private IImage? IconFor(FileEntryViewModel entry) =>
        entry.IsDirectory ? Icons.For(entry.Entry.Kind) : _thumbnails.Get(entry.FullPath) ?? Icons.For(entry.Entry.Kind);

    /// <summary>Arrives on a worker thread; the swap has to happen on the UI thread.</summary>
    private void OnThumbnailReady(object? sender, string path)
    {
        try
        {
            this.BeginInvoke(() => this.Apply(path));
        }
        catch (InvalidOperationException)
        {
            // The window went away while the decode was in flight.
        }
    }

    private void Apply(string path)
    {
        for (var i = 0; i < this.Items.Count; ++i)
        {
            if (this.Items[i].Tag is not FileEntryViewModel entry || entry.FullPath != path)
                continue;

            this.Items[i].Image = _thumbnails.Get(path) ?? this.Items[i].Image;
            this.Invalidate();
            return;
        }
    }

    // ---- selection, shared with the details view through the same view-model ----

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressSelection)
            return;

        var selected = this.SelectedItems.Select(item => item.Tag).OfType<FileEntryViewModel>().ToList();
        _tab.SetSelection(selected);
        _tab.SelectedEntry = selected.Count > 0 ? selected[0] : null;
    }

    private void SyncSelection()
    {
        if (_tab.SelectedEntry is not { } entry)
            return;

        for (var i = 0; i < this.Items.Count; ++i)
        {
            if (!ReferenceEquals(this.Items[i].Tag, entry))
                continue;
            if (this.SelectedIndex == i)
                return;

            _suppressSelection = true;
            this.SelectedIndex = i;
            _suppressSelection = false;
            return;
        }
    }

    private void ActivateSelected()
    {
        if (_tab.SelectedEntry is { } selected)
            _tab.OpenCommand.Execute(selected);
    }

    // ---- keyboard, matching the details view ----

    /// <inheritdoc/>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _dragFrom = e.Button == MouseButtons.Left && !e.Control && !e.Shift && this.SelectedItems.Count > 0
            ? new System.Drawing.Point(e.X, e.Y)
            : new System.Drawing.Point(-1, -1);
        base.OnMouseDown(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragFrom.X >= 0
            && (Math.Abs(e.X - _dragFrom.X) > DragThreshold || Math.Abs(e.Y - _dragFrom.Y) > DragThreshold))
        {
            var paths = this.SelectedItems.Select(item => item.Tag).OfType<FileEntryViewModel>()
                .Select(entry => entry.FullPath).ToList();
            _dragFrom = new System.Drawing.Point(-1, -1);
            if (paths.Count > 0)
                this.DoDragDrop(new FileDrag(paths, _tab.CurrentPath), DragDropEffects.Copy | DragDropEffects.Move);
            return;
        }

        base.OnMouseMove(e);
    }

    /// <inheritdoc/>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragFrom = new System.Drawing.Point(-1, -1);
        base.OnMouseUp(e);
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter when !e.Alt:
                this.ActivateSelected();
                e.Handled = true;
                return;
            case Keys.Delete:
                _shell.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                return;
            case Keys.F2:
                _shell.RenameSelectedCommand.Execute(null);
                e.Handled = true;
                return;
        }

        base.OnKeyDown(e);
    }

    /// <inheritdoc/>
    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar) || e.KeyChar == ' ')
        {
            base.OnKeyPress(e);
            return;
        }

        var names = _tab.Entries.Select(entry => entry.Name).ToList();
        var index = _typeAhead.Next(e.KeyChar, names, this.SelectedIndex, DateTime.UtcNow);
        if (index < 0)
        {
            base.OnKeyPress(e);
            return;
        }

        this.SelectedIndex = index;
        this.EnsureVisible(index);
        e.Handled = true;
    }
}
