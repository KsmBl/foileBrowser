using System.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// Turns a cell's value into the colour its background is tinted with (PRD §6.1).
/// </summary>
/// <remarks>
/// <para>
/// The colour is blended into the row background rather than replacing it, at a fraction low enough
/// that the text on top stays the theme's own and stays readable. That is what lets this work on a
/// dark desktop and a light one from the same code: the tint leans the background towards a hue, it
/// does not decide what the background is.
/// </para>
/// <para>
/// Only the heated column's cells are tinted, not the whole row. Two reasons: colour tags already own
/// the row-wide colour (§6.7) and would be drowned out, and tinting per column is what makes more
/// than one heat map at a time readable — each column carries its own scale, side by side.
/// </para>
/// </remarks>
internal static class Heat
{
    /// <summary>How far the background is moved towards the heat colour.</summary>
    private const double Tint = 0.34;

    /// <summary>Saturation and value of the heat hues before blending.</summary>
    private const double Saturation = 0.85;
    private const double Value = 1.0;

    /// <summary>
    /// The tint for a value ranked between the smallest and largest in the folder: cold blue at the
    /// bottom of the range through green and yellow to hot red at the top.
    /// </summary>
    /// <returns>Null when there is nothing to rank — no value, or every row sharing one value, in
    /// which case a gradient would say something the data does not.</returns>
    internal static Color? Numeric(double? value, double min, double max, Color background)
    {
        if (value is not { } v || max <= min || double.IsNaN(v))
            return null;

        var t = Math.Clamp((v - min) / (max - min), 0, 1);

        // 210° (blue) down to 0° (red), which passes through cyan, green and yellow on the way — the
        // ramp a heat map is read as, and monotonic in hue so neighbouring values look neighbouring.
        return Blend(FromHue(210 - (210 * t)), background);
    }

    /// <summary>
    /// The tint for one distinct value, stable across runs and folders so a given extension is always
    /// the same colour. Null for an empty value, which is an absence rather than a category.
    /// </summary>
    internal static Color? Category(string? value, Color background)
        => string.IsNullOrEmpty(value) ? null : Blend(FromHue(HueOf(value)), background);

    /// <summary>
    /// A hue for a string, spread around the circle by the golden angle so values that hash to
    /// neighbouring numbers still land far apart.
    /// </summary>
    /// <remarks>
    /// FNV-1a rather than <see cref="string.GetHashCode()"/>, which is seeded per process: the same
    /// folder would colour differently on every launch, and a colour that means something has to mean
    /// the same thing tomorrow.
    /// </remarks>
    private static double HueOf(string value)
    {
        const uint Offset = 2166136261;
        const uint Prime = 16777619;

        var hash = Offset;
        foreach (var c in value)
        {
            hash ^= char.ToLowerInvariant(c);
            hash *= Prime;
        }

        return (hash % 3600 / 10.0 * 137.508) % 360;
    }

    /// <summary>A fully saturated colour at the given hue.</summary>
    private static Color FromHue(double hue)
    {
        var sector = ((hue % 360) + 360) % 360 / 60;
        var f = sector - Math.Floor(sector);
        var p = Value * (1 - Saturation);
        var q = Value * (1 - (Saturation * f));
        var t = Value * (1 - (Saturation * (1 - f)));

        var (r, g, b) = (int)Math.Floor(sector) switch
        {
            0 => (Value, t, p),
            1 => (q, Value, p),
            2 => (p, Value, t),
            3 => (p, q, Value),
            4 => (t, p, Value),
            _ => (Value, p, q),
        };

        return Color.FromArgb(Channel(r), Channel(g), Channel(b));
    }

    /// <summary>Moves <paramref name="background"/> a fraction of the way towards <paramref name="color"/>.</summary>
    private static Color Blend(Color color, Color background) => Color.FromArgb(
        Mix(background.R, color.R),
        Mix(background.G, color.G),
        Mix(background.B, color.B));

    private static int Mix(int from, int to) => Channel(((from + ((to - from) * Tint)) / 255.0));

    private static int Channel(double unit) => Math.Clamp((int)Math.Round(unit * 255), 0, 255);
}
