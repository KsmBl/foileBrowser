namespace FoileBrowser.Models;

/// <summary>How file sizes are rendered (PRD §6.2, quick-switchable).</summary>
public enum SizeUnit
{
    /// <summary>1024-based with IEC units: KiB, MiB, GiB…</summary>
    Binary,

    /// <summary>1000-based with SI units: KB, MB, GB…</summary>
    Decimal,

    /// <summary>Exact byte count, grouped (e.g. 1,234,567 B).</summary>
    Bytes,
}

/// <summary>How modified dates are rendered (PRD §6.1, quick-switchable).</summary>
public enum DateDisplay
{
    /// <summary>Fixed timestamp, e.g. 2026-07-16 08:16.</summary>
    Absolute,

    /// <summary>Relative to now, e.g. "5 min ago", "yesterday".</summary>
    Relative,
}

/// <summary>
/// Shared, mutable view preferences for how sizes and dates are shown in file lists. A single
/// instance is passed to every entry so a quick toggle re-renders them all consistently.
/// </summary>
public sealed class DisplayOptions
{
    public SizeUnit SizeUnit { get; set; } = SizeUnit.Binary;
    public DateDisplay DateDisplay { get; set; } = DateDisplay.Absolute;
}
