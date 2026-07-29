using System.Drawing;
using FoileBrowser.Services;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// The recent transfer rate drawn as a filled line, oldest sample at the left (PRD §6.3).
/// </summary>
/// <remarks>
/// A number tells you the speed; the shape tells you whether it is holding, collapsing into a wall
/// of small files, or sawing between two devices — which is the thing a person actually wants to
/// know before deciding whether to wait. The scale is the highest rate seen in the window, so the
/// curve fills the box whatever the hardware, and the average is drawn across it as a flat line so
/// the two can be compared without reading either number.
/// </remarks>
public sealed class SpeedGraph : OwnerDrawnControl
{
    private TransferRate? _rate;

    /// <summary>The tracker to draw. Setting it repaints.</summary>
    public TransferRate? Rate
    {
        get => _rate;
        set
        {
            _rate = value;
            this.Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var theme = this.Theme;
        var box = new Rectangle(0, 0, this.Width, this.Height);

        g.FillRectangle(theme.FieldBackground, box);
        g.DrawRectangle(theme.Border, new Rectangle(0, 0, this.Width - 1, this.Height - 1));

        var rate = _rate;
        if (rate is null || box.Width < 4 || box.Height < 4)
            return;

        var samples = rate.Samples;
        var peak = rate.Peak;
        if (samples.Count < 2 || peak <= 0)
            return;

        // Fixed to the ring's capacity rather than to how many samples exist, so the curve grows in
        // from the left instead of stretching and re-scaling horizontally on every frame.
        var step = (double)(box.Width - 2) / (TransferRate.Capacity - 1);
        var baseline = box.Height - 2;

        // The fill has to read as a shaded area on a light desktop and a dark one, so it is the accent
        // leaned towards the background rather than a theme colour that happens to contrast on one of
        // them — HeaderBackground is near-white on the default light theme and vanished entirely.
        var accent = theme.Accent;
        var field = theme.FieldBackground;
        var fillColor = Color.FromArgb(
            field.R + ((accent.R - field.R) / 4),
            field.G + ((accent.G - field.G) / 4),
            field.B + ((accent.B - field.B) / 4));

        var previousX = 1;
        var previousY = baseline;
        for (var i = 0; i < samples.Count; ++i)
        {
            var x = 1 + (int)Math.Round(i * step);
            var y = baseline - (int)Math.Round(samples[i] / peak * (box.Height - 4));

            if (i > 0)
            {
                // Filled under the curve first, one column at a time and following the slope between
                // the two points rather than squaring off at the newer one — a shaded area reads as a
                // quantity where a bare line reads as a boundary. Then the line on top of it.
                var span = Math.Max(1, x - previousX);
                for (var fill = previousX; fill <= x; ++fill)
                {
                    var height = previousY + ((y - previousY) * (fill - previousX) / span);
                    g.DrawLine(fillColor, fill, height + 1, fill, baseline);
                }

                g.DrawLine(theme.Accent, previousX, previousY, x, y, 2);
            }

            previousX = x;
            previousY = y;
        }

        var average = rate.Average;
        if (average <= 0 || average > peak)
            return;

        // Dashed, so it reads as a reference rather than as another reading, and in the text colour
        // because DisabledText is too faint to find against the fill.
        var averageY = baseline - (int)Math.Round(average / peak * (box.Height - 4));
        for (var x = 1; x < box.Width - 2; x += 6)
            g.DrawLine(theme.ControlText, x, averageY, Math.Min(x + 3, box.Width - 2), averageY);
    }
}
