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

    public bool IsToolbarVisible { get; set; } = true;

    /// <summary>Ids of global-toolbar buttons the user has hidden (PRD §6.8). Empty = show all.</summary>
    public List<string> HiddenToolbarButtons { get; set; } = [];

    /// <summary>Whether each pane's filter/search bar is shown by default; when false, Ctrl+F reveals it (PRD §6.4).</summary>
    public bool SearchBarVisible { get; set; } = true;

    /// <summary>Master switch for the destructive "Format…" action on drives/partitions — off by default (PRD §6.10).</summary>
    public bool EnableDiskFormatting { get; set; }

    /// <summary>Filesystem ids offered in the format dialog (e.g. "ext4"). Empty = offer every installed type (PRD §6.10).</summary>
    public List<string> FormatFilesystems { get; set; } = [];

    /// <summary>Size display mode: "Binary", "Decimal", or "Bytes" (PRD §6.2).</summary>
    public string SizeUnit { get; set; } = "Binary";

    /// <summary>Date display mode: "Absolute" or "Relative" (PRD §6.1).</summary>
    public string DateFormat { get; set; } = "Absolute";

    // ---- copy engine tunables (PRD §6.3) ----

    /// <summary>Overlapped copy chunk size in KiB.</summary>
    public int CopyBufferKiB { get; set; } = 1024;

    /// <summary>Sequential-slurp block size in KiB, used on mechanical/optical media.</summary>
    public int SequentialBufferKiB { get; set; } = 8192;

    /// <summary>"Auto", "Overlapped", or "Sequential".</summary>
    public string CopyStrategy { get; set; } = "Auto";

    /// <summary>Projects the persisted copy settings onto the engine's option record.</summary>
    public CopyOptions ToCopyOptions() => new()
    {
        BufferSize = Math.Max(64, CopyBufferKiB) * 1024,
        SequentialBufferSize = Math.Max(64, SequentialBufferKiB) * 1024,
        Strategy = Enum.TryParse<global::FoileBrowser.Models.CopyStrategy>(
            CopyStrategy, ignoreCase: true, out var s) ? s : global::FoileBrowser.Models.CopyStrategy.Auto,
    };

    /// <summary>Pinned sidebar favorite paths (PRD §6.2).</summary>
    public List<string> Favorites { get; set; } = [];

    /// <summary>Names of built-in favorites (Home/Desktop/Documents/Downloads) the user has removed (PRD §6.2).</summary>
    public List<string> HiddenDefaultFavorites { get; set; } = [];

    /// <summary>Path → color-tag hex (PRD §6.7 color tags).</summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    /// <summary>Open tabs to restore on next launch (PRD §6.2 "restored across restart").</summary>
    public SessionLayout Session { get; set; } = new();

    /// <summary>Command-id → hotkey gesture overrides (e.g. "tab.new" → "Ctrl+T"). Empty = ship defaults (PRD §6.6).</summary>
    public Dictionary<string, string> Keybinds { get; set; } = new();
}

public sealed class SessionLayout
{
    // Legacy two-pane fields, kept so old settings files still restore (superseded by Panes).
    public List<string> LeftTabs { get; set; } = [];
    public int LeftActiveIndex { get; set; }
    public List<string> RightTabs { get; set; } = [];
    public int RightActiveIndex { get; set; }

    /// <summary>Open panes (any number) to restore, each with its tab paths (PRD §6.2).</summary>
    public List<PaneSession> Panes { get; set; } = [];
}

public sealed class PaneSession
{
    public List<string> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }
}
