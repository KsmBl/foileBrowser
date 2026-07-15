namespace FoileBrowser.Models;

/// <summary>
/// The full, serializable application configuration persisted as portable JSON (PRD §6.8).
/// Plain mutable properties keep System.Text.Json (de)serialization trivial.
/// </summary>
public sealed class AppSettings
{
    /// <summary>"System", "Light", or "Dark" (PRD §6.8 themes).</summary>
    public string ThemeVariant { get; set; } = "System";

    public string AccentColor { get; set; } = "#3D8BFD";

    public double FontSize { get; set; } = 13;

    /// <summary>Row height in px — the row-density setting (PRD §6.8).</summary>
    public double RowHeight { get; set; } = 24;

    public bool IsDualPane { get; set; } = true;

    public bool IsInspectorOpen { get; set; } = true;

    /// <summary>Pinned sidebar favorite paths (PRD §6.2).</summary>
    public List<string> Favorites { get; set; } = [];

    /// <summary>Path → color-tag hex (PRD §6.7 color tags).</summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>Open tabs to restore on next launch (PRD §6.2 "restored across restart").</summary>
    public SessionLayout Session { get; set; } = new();
}

public sealed class SessionLayout
{
    public List<string> LeftTabs { get; set; } = [];
    public int LeftActiveIndex { get; set; }
    public List<string> RightTabs { get; set; } = [];
    public int RightActiveIndex { get; set; }
}
