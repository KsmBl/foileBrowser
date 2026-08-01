using System.Drawing;
using FoileBrowser.Models;
using FoileBrowser.Services;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>How a picture is sized to the panel when it arrives, until the wheel says otherwise.</summary>
public enum PreviewScale
{
    /// <summary>The whole picture, as large as fits.</summary>
    Fit,

    /// <summary>One image pixel to one screen pixel.</summary>
    ActualSize,

    /// <summary>As wide as the panel; taller pictures run off the bottom and are panned.</summary>
    FitWidth,

    /// <summary>As tall as the panel; wider pictures run off the side.</summary>
    FitHeight,
}

/// <summary>
/// The inspector's body (PRD §6.5): a pannable, zoomable picture with a filmstrip when the selection
/// holds more than one, or the text of whatever else was picked.
/// </summary>
/// <remarks>
/// This replaces a fixed-zoom picture box. That box could only ever show one image scaled to the
/// panel, which meant a preview was something to glance at rather than something to look at — no way
/// in to a detail, no way through a folder of photographs, and no say in how a picture was sized.
/// </remarks>
public sealed class PreviewPane : Panel
{
    private const int _BarHeight = 28;

    private readonly Label _title = new() { Dock = DockStyle.Top, Bounds = new(0, 0, 0, 22), Padding = new(6, 4, 6, 0) };
    private readonly Label _info = new() { Dock = DockStyle.Top, Bounds = new(0, 0, 0, 20), ForeColor = Color.Gray, Padding = new(6, 0, 6, 4) };
    private readonly Label _placeholder = new()
    {
        Dock = DockStyle.Fill,
        Text = "Select an item to preview",
        ForeColor = Color.Gray,
        TextAlign = ContentAlignment.MiddleCenter,
    };

    private readonly TableLayoutPanel _bar = new()
    {
        Dock = DockStyle.Top,
        Bounds = new(0, 0, 0, _BarHeight),
        ColumnCount = 5,
        RowCount = 1,
        Visible = false,
    };

    private readonly Button _previous = new() { Image = Icons.BackIcon, Margin = new(1) };
    private readonly Button _next = new() { Image = Icons.ForwardIcon, Margin = new(1) };
    private readonly Label _counter = new() { TextAlign = ContentAlignment.MiddleCenter, Margin = new(2) };
    private readonly ComboBox _scale = new() { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new(2) };
    private readonly Label _zoomLabel = new() { TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.Gray, Margin = new(2) };

    /// <summary>
    /// A <see cref="ZoomPanel"/> that says when the user has taken hold of it, and steps through the
    /// selection on the paging keys.
    /// </summary>
    /// <remarks>
    /// The base class handles pan and wheel-zoom in overrides that do not chain, so there is no event
    /// to subscribe to from outside; chaining through them here is what makes both observable. The
    /// arrow keys are left to the panel for panning, so paging is what walks the filmstrip.
    /// </remarks>
    private sealed class InteractiveZoomPanel : ZoomPanel
    {
        /// <summary>Raised once the picture has been panned or zoomed by hand.</summary>
        public event EventHandler? Grabbed;

        /// <summary>Raised with -1 or +1 to walk the selection.</summary>
        public event EventHandler<int>? Stepped;

        /// <summary>Raised the first time the panel paints at a size it has not painted at before.</summary>
        public event EventHandler? Resized;

        private Size _painted;

        /// <inheritdoc/>
        /// <remarks>
        /// A control cannot see its own bounds change — <c>Control</c> raises nothing for it and the
        /// hook that would is internal to the toolkit — so the first paint at a new size stands in.
        /// It matters at startup above all: an image adopted before the panel has been laid out is
        /// framed against a viewport of nothing, which leaves it parked off the edge of a panel that
        /// then appears to be empty.
        /// </remarks>
        protected override void OnPaint(PaintEventArgs e)
        {
            var size = new Size(this.Width, this.Height);
            if (size != _painted)
            {
                _painted = size;
                this.Resized?.Invoke(this, EventArgs.Empty);
            }

            base.OnPaint(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button is MouseButtons.Left or MouseButtons.Middle)
                this.Grabbed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            this.Grabbed?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.PageDown:
                    this.Stepped?.Invoke(this, 1);
                    e.Handled = true;
                    return;
                case Keys.PageUp:
                    this.Stepped?.Invoke(this, -1);
                    e.Handled = true;
                    return;
                default:
                    base.OnKeyDown(e);
                    return;
            }
        }
    }

    private readonly InteractiveZoomPanel _view = new() { Dock = DockStyle.Fill, Visible = false, ShowZoomControl = false };
    private readonly PreviewStrip _strip;
    private readonly TextBox _text = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, AcceptsReturn = true, Visible = false };

    private IReadOnlyList<string> _paths = [];
    private int _index;
    private IImage? _current;
    private bool _suppress;

    public PreviewPane(ThumbnailService thumbnails)
    {
        _strip = new PreviewStrip(thumbnails)
        {
            Dock = DockStyle.Bottom,
            Bounds = new(0, 0, 0, PreviewStrip.CellHeight),
            Visible = false,
        };

        this.BuildBar();

        // Reverse order: the last child added claims its edge first (see Control.OnLayout).
        this.Controls.Add(_placeholder);
        this.Controls.Add(_view);
        this.Controls.Add(_text);
        this.Controls.Add(_strip);
        this.Controls.Add(_bar);
        this.Controls.Add(_info);
        this.Controls.Add(_title);

        _strip.Picked += (_, index) => this.ShowAt(index);
        _view.ZoomChanged += (_, _) => _zoomLabel.Text = $"{_view.Zoom * 100:0}%";
        _view.Grabbed += (_, _) => _touched = true;
        _view.Stepped += (_, delta) => this.Step(delta);
        // Sizing only re-frames a picture the user has not taken hold of; once they have panned or
        // zoomed, a resize leaving it where they put it is the whole point.
        _view.Resized += (_, _) => this.ApplyScale(onlyIfUntouched: true);
    }

    private void BuildBar()
    {
        _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        _bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));

        _previous.Click += (_, _) => this.Step(-1);
        _next.Click += (_, _) => this.Step(1);

        foreach (var mode in new[] { "Fit", "Actual size", "Fit width", "Fit height" })
            _scale.Items.Add(mode);
        _scale.SelectedIndex = 0;
        _scale.SelectedIndexChanged += (_, _) =>
        {
            if (_suppress)
                return;

            _touched = false;
            this.ApplyScale(onlyIfUntouched: false);
        };

        _bar.Controls.AddRange(_previous, _next, _counter, _scale, _zoomLabel);
    }

    /// <summary>Whether the user has panned or zoomed away from the mode's own framing.</summary>
    private bool _touched;

    /// <summary>The sizing policy for a picture as it arrives.</summary>
    public PreviewScale Scale
    {
        get => (PreviewScale)_scale.SelectedIndex;
        set
        {
            _suppress = true;
            _scale.SelectedIndex = (int)value;
            _suppress = false;
            _touched = false;
            this.ApplyScale(onlyIfUntouched: false);
        }
    }

    /// <summary>Drops the thumbnail subscription when the panel goes away.</summary>
    public void Detach() => _strip.Detach();

    /// <summary>
    /// Re-frames the picture after the panel has changed size, unless the user has taken hold of it.
    /// </summary>
    /// <remarks>
    /// Called by whoever moved the edge — the window on a resize, the splitter on a drag. A control
    /// cannot see its own bounds change from here: <c>Control</c> exposes no event for it, and the
    /// hook that would is internal to the toolkit.
    /// </remarks>
    public void Reframe() => this.ApplyScale(onlyIfUntouched: true);

    /// <summary>Renders a preview, or the empty-state hint when there is none.</summary>
    public void Show(PreviewResult? preview)
    {
        _paths = preview?.ImagePaths ?? [];
        _index = 0;

        _title.Text = preview?.Title ?? string.Empty;
        _info.Text = preview?.Info ?? string.Empty;

        if (preview is null)
        {
            this.ShowNothing();
            return;
        }

        _placeholder.Visible = false;

        if (preview.HasImage)
        {
            _strip.SetItems(_paths, 0);
            Ui.SetDockedExtent(_strip, preview.HasManyImages, PreviewStrip.CellHeight);
            Ui.SetDockedExtent(_bar, true, _BarHeight);
            this.ShowAt(0);
            return;
        }

        this.ReleaseImage();
        _view.Visible = false;
        Ui.SetDockedExtent(_strip, false, PreviewStrip.CellHeight);
        Ui.SetDockedExtent(_bar, false, _BarHeight);
        _text.Text = preview.Text ?? string.Empty;
        _text.Visible = true;
        this.PerformLayout();
    }

    /// <summary>Steps through a multi-image selection; the ends do not wrap.</summary>
    public void Step(int delta)
    {
        if (_paths.Count > 1)
            this.ShowAt(Math.Clamp(_index + delta, 0, _paths.Count - 1));
    }

    private void ShowNothing()
    {
        this.ReleaseImage();
        _placeholder.Visible = true;
        _view.Visible = false;
        _text.Visible = false;
        Ui.SetDockedExtent(_strip, false, PreviewStrip.CellHeight);
        Ui.SetDockedExtent(_bar, false, _BarHeight);
    }

    private void ReleaseImage()
    {
        _view.Image = null;
        _current?.Dispose();
        _current = null;
    }

    /// <summary>Decodes and shows the image at <paramref name="index"/> in the current selection.</summary>
    private void ShowAt(int index)
    {
        if (index < 0 || index >= _paths.Count)
            return;

        _index = index;
        _strip.Current = index;
        _counter.Text = _paths.Count > 1 ? $"{index + 1} / {_paths.Count}" : string.Empty;
        _previous.Enabled = index > 0;
        _next.Enabled = index < _paths.Count - 1;

        this.ReleaseImage();

        var failure = PreviewImage.Failure.None;
        if (PreviewImage.Load(_paths[index], out failure) is { } image)
        {
            _current = image;
            _view.Image = image;      // adopts the size and fits it
            _view.Visible = true;
            _text.Visible = false;
            // A control switched on after the last layout pass has no bounds yet, and a fill panel
            // with no bounds paints nothing at all — which is a blank preview, not an obvious bug.
            this.PerformLayout();
            _touched = false;
            this.ApplyScale(onlyIfUntouched: false);
            _zoomLabel.Text = $"{_view.Zoom * 100:0}%";
            return;
        }

        // An image that would not decode still gets its details, with a note in place of the pixels
        // rather than an empty panel.
        _view.Visible = false;
        _text.Text = failure switch
        {
            PreviewImage.Failure.TooLarge => "(no preview: the image is too large to decode)",
            _ => "(no preview: the image could not be read)",
        };
        _text.Visible = true;
        this.PerformLayout();
    }

    /// <summary>Frames the picture the way the chosen mode asks for.</summary>
    private void ApplyScale(bool onlyIfUntouched)
    {
        if (_view.Image is null || (onlyIfUntouched && _touched))
            return;

        var content = _view.ContentSize;
        var viewport = _view.Viewport;
        if (content.Width <= 0 || content.Height <= 0 || viewport.Width <= 0 || viewport.Height <= 0)
            return;

        switch (this.Scale)
        {
            case PreviewScale.ActualSize:
                _view.ActualSize();
                break;
            case PreviewScale.FitWidth:
                _view.FitToWindow(); // centres first, so the axis that is not being fitted is sane
                _view.Zoom = (double)viewport.Width / content.Width;
                break;
            case PreviewScale.FitHeight:
                _view.FitToWindow();
                _view.Zoom = (double)viewport.Height / content.Height;
                break;
            default:
                _view.FitToWindow();
                // Fitting shrinks, but does not enlarge: a 40x24 icon blown to 843% to fill the panel
                // is not what "fit" means to anyone looking at it.
                if (_view.Zoom > 1.0)
                    _view.Zoom = 1.0;
                break;
        }

        _zoomLabel.Text = $"{_view.Zoom * 100:0}%";
    }
}
