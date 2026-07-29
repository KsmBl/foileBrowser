using System.Globalization;

namespace FoileBrowser.Models;

/// <summary>Renders sizes and dates according to the current <see cref="DisplayOptions"/> (PRD §6.1/§6.2).</summary>
public static class ValueFormat
{
    private static readonly string[] BinaryUnits = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
    private static readonly string[] DecimalUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    // Every format below names the invariant culture rather than letting the ambient one decide.
    // The app already renders this way, but only because InvariantGlobalization is set in the csproj
    // to drop ICU -- so the separators were a side effect of a size optimisation rather than a
    // decision, and read as "1,5 MB" anywhere that switch does not apply, a test host included.
    // Saying it here is what keeps the two agreeing, and is the single place to revisit when the
    // German localisation that csproj comment defers actually arrives.
    public static string Size(long bytes, SizeUnit unit) => unit switch
    {
        SizeUnit.Bytes => string.Create(CultureInfo.InvariantCulture, $"{bytes:#,##0} B"),
        SizeUnit.Decimal => Scale(bytes, 1000, DecimalUnits),
        _ => Scale(bytes, 1024, BinaryUnits),
    };

    private static string Scale(long bytes, double @base, string[] units)
    {
        if (bytes < @base)
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} {units[0]}");

        double value = bytes;
        var unit = 0;
        while (value >= @base && unit < units.Length - 1)
        {
            value /= @base;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }

    public static string Date(DateTimeOffset? modified, DateDisplay mode) =>
        Date(modified, mode, DateTimeOffset.Now);

    /// <summary><paramref name="now"/> is injectable so relative formatting is testable.</summary>
    public static string Date(DateTimeOffset? modified, DateDisplay mode, DateTimeOffset now)
    {
        if (modified is not { } dt)
            return string.Empty;
        if (mode == DateDisplay.Absolute)
            return dt.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

        var delta = now - dt;
        if (delta < TimeSpan.Zero)
            return "just now"; // clock skew / future timestamps

        var minutes = delta.TotalMinutes;
        if (minutes < 1) return "just now";
        if (minutes < 60) return $"{(int)minutes} min ago";

        var hours = delta.TotalHours;
        if (hours < 24) return $"{(int)hours} h ago";

        var days = (int)delta.TotalDays;
        if (days == 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        if (days < 30) return Plural(days / 7, "week");
        if (days < 365) return Plural(days / 30, "month");
        return Plural(days / 365, "year");
    }

    private static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
}
