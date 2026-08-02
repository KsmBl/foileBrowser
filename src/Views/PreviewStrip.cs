using System.Drawing;
using FoileBrowser.Services;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// The row of thumbnails under a multi-image preview (PRD §6.5): what else is in the selection, and
/// the way to jump straight to one of them.
/// </summary>
/// <remarks>
/// Drawn rather than assembled from a list control because it is one row that scrolls sideways and
/// keeps the current picture in view, which no stock view mode does. Thumbnails come from the same
/// background decoder the gallery uses, so a strip over a folder of photographs costs one decode each
/// and nothing on the second look; until one arrives its slot is an outline, and the strip repaints
/// as they land.
/// </remarks>
public sealed class PreviewStrip : OwnerDrawnControl
{
    /// <summary>Height of a thumbnail cell, which sets the control's own height.</summary>
    public const int CellHeight = 68;

    private const int _CellWidth = 84;
    private const int _Gap = 4;

    private readonly ThumbnailService _thumbnails;
    private IReadOnlyList<string> _paths = [];
    private int _current;
    private int _offset;

    public PreviewStrip(ThumbnailService thumbnails)
    {
        _thumbnails = thumbnails;
        _thumbnails.Ready += this.OnThumbnailReady;
    }

    /// <summary>Raised when a thumbnail is clicked.</summary>
    public event EventHandler<int>? Picked;

    /// <summary>Drops the decoder subscription when the panel goes away.</summary>
    public void Detach() => _thumbnails.Ready -= this.OnThumbnailReady;

    /// <summary>The pictures to show, and which of them is on screen above.</summary>
    public void SetItems(IReadOnlyList<string> paths, int current)
    {
        _paths = paths;
        _current = current;
        this.ScrollCurrentIntoView();
        this.Invalidate();
    }

    /// <summary>Moves the highlight without rebuilding the row.</summary>
    public int Current
    {
        get => _current;
        set
        {
            if (_current == value)
                return;

            _current = value;
            this.ScrollCurrentIntoView();
            this.Invalidate();
        }
    }

    private int TotalWidth => (_paths.Count * (_CellWidth + _Gap)) + _Gap;

    /// <summary>Keeps the highlighted cell inside the visible run, scrolling the least that will do.</summary>
    private void ScrollCurrentIntoView()
    {
        if (_paths.Count == 0 || this.Width <= 0)
            return;

        var left = _Gap + (_current * (_CellWidth + _Gap));
        var right = left + _CellWidth;
        _offset = Math.Min(_offset, left - _Gap);
        _offset = Math.Max(_offset, right + _Gap - this.Width);
        _offset = Math.Clamp(_offset, 0, Math.Max(0, this.TotalWidth - this.Width));
    }

    private void OnThumbnailReady(object? sender, string path)
    {
        // Only repaint for a picture this strip is actually showing.
        if (_paths.Contains(path, StringComparer.Ordinal))
            this.BeginInvoke(this.Invalidate);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.FillRectangle(this.Theme.ControlBackground, new Rectangle(0, 0, this.Width, this.Height));

        for (var i = 0; i < _paths.Count; ++i)
        {
            var cell = new Rectangle(_Gap + (i * (_CellWidth + _Gap)) - _offset, _Gap, _CellWidth, CellHeight - (_Gap * 2));
            if (cell.Right < 0)
                continue;
            if (cell.Left > this.Width)
                break;

            if (i == _current)
                g.FillRectangle(this.Theme.SelectionBackground, cell);

            // CurrentFrameOf resolves a decoded thumbnail to the frame due now. Handing the image
            // itself to DrawImage draws nothing at all: every thumbnail arrives as an AnimatedImage,
            // which has no pixels of its own — so the strip laid out its cells, highlighted the
            // current one and painted no pictures in any of them.
            if (this.CurrentFrameOf(_thumbnails.Get(_paths[i])) is { } image)
            {
                // Fit inside the cell without distorting: a portrait and a landscape both stay square-on.
                var scale = Math.Min((double)cell.Width / image.Width, (double)cell.Height / image.Height);
                var size = new Size(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
                g.DrawImage(image, new Rectangle(
                    cell.X + ((cell.Width - size.Width) / 2),
                    cell.Y + ((cell.Height - size.Height) / 2),
                    size.Width,
                    size.Height));
            }
            else
                g.DrawRectangle(this.Theme.GridLine, cell);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || _paths.Count == 0)
            return;

        var index = (e.X + _offset - _Gap) / (_CellWidth + _Gap);
        if (index >= 0 && index < _paths.Count)
            this.Picked?.Invoke(this, index);
    }

    /// <summary>The wheel runs the strip sideways, since there is nowhere for it to go vertically.</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var limit = Math.Max(0, this.TotalWidth - this.Width);
        if (limit == 0)
            return;

        _offset = Math.Clamp(_offset - (e.Delta / 120 * (_CellWidth + _Gap)), 0, limit);
        this.Invalidate();
    }
}
