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

    /// <summary>Custom left-to-right order of toolbar button ids (PRD §6.8). Empty = default order.</summary>
    public List<string> ToolbarOrder { get; set; } = [];

    /// <summary>
    /// Whether each pane's subtree-search row is shown by default; when false, Ctrl+F reveals it
    /// (PRD §6.4). Off by default because searching a subtree is occasional and the row costs its
    /// height in every pane for the whole session — the everyday filter box lives in the nav bar
    /// and is always there either way. The name predates that split; it is what is on disk.
    /// </summary>
    public bool SearchBarVisible { get; set; }

    /// <summary>
    /// Where a folder handed over by a second launch is opened (PRD §6.12): <c>Tab</c> in the active
    /// pane's tab strip, <c>Pane</c> split beside it through the docking layout, or <c>Window</c> as
    /// its own top-level window sharing this process.
    /// </summary>
    public string OpenHandoffIn { get; set; } = "Tab";

    /// <summary>Which way a pane lists a folder to begin with: <c>Details</c> rows or a <c>Gallery</c>
    /// of thumbnails (PRD §6.2).</summary>
    public string ViewMode { get; set; } = "Details";

    /// <summary>
    /// Terminal launched by "Open terminal here" (PRD §6.9). Empty auto-detects. May be a bare
    /// executable ("kitty") or a full command line, where <c>{dir}</c> is replaced by the folder.
    /// </summary>
    public string TerminalCommand { get; set; } = string.Empty;

    // Which navigation-sidebar sections are shown (PRD §6.2). The folder tree is off by default.
    public bool SidebarShowFavorites { get; set; } = true;
    public bool SidebarShowDrives { get; set; } = true;
    public bool SidebarShowDevices { get; set; } = true;
    public bool SidebarShowTree { get; set; }

    /// <summary>Folder-tree root: "HomeAndDrives", "Root" (/), or "Current" (the active pane's folder) (PRD §6.2).</summary>
    public string TreeRoot { get; set; } = "HomeAndDrives";

    /// <summary>Custom top-to-bottom order of sidebar section ids (favorites/drives/devices/tree) (PRD §6.2). Empty = default.</summary>
    public List<string> SidebarSectionOrder { get; set; } = [];

    /// <summary>Master switch for the destructive "Format…" action on drives/partitions — off by default (PRD §6.10).</summary>
    public bool EnableDiskFormatting { get; set; }

    /// <summary>Filesystem ids offered in the format dialog (e.g. "ext4"). Empty = offer every installed type (PRD §6.10).</summary>
    public List<string> FormatFilesystems { get; set; } = [];

    /// <summary>Size display mode: "Binary", "Decimal", or "Bytes" (PRD §6.2).</summary>
    public string SizeUnit { get; set; } = "Binary";

    /// <summary>Date display mode: "Absolute" or "Relative" (PRD §6.1).</summary>
    public string DateFormat { get; set; } = "Absolute";

    /// <summary>Visible file-list columns, in order, with their widths (PRD §6.1). Empty = defaults.</summary>
    public List<ColumnState> Columns { get; set; } = [];

    /// <summary>Ids of the columns drawn as a heat map (PRD §6.1). Any number of them at once, since
    /// each carries its own scale in its own cells. Empty = none, which is the default.</summary>
    public List<string> HeatColumns { get; set; } = [];

    /// <summary>Folders visited recently, most recent first (PRD §6.1). Feeds the path bar's
    /// suggestions and the Go menu; bounded so the settings file cannot grow without limit.</summary>
    public List<string> RecentFolders { get; set; } = [];

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

/// <summary>Persisted state of one visible file-list column (PRD §6.1).</summary>
public sealed class ColumnState
{
    public string Id { get; set; } = string.Empty;
    public double Width { get; set; }
}

public sealed class SessionLayout
{
    // Legacy two-pane fields, kept so old settings files still restore (superseded by Panes/Tree).
    public List<string> LeftTabs { get; set; } = [];
    public int LeftActiveIndex { get; set; }
    public List<string> RightTabs { get; set; } = [];
    public int RightActiveIndex { get; set; }

    /// <summary>Open panes (any number) to restore, each with its tab paths — flat mirror of the tree (PRD §6.2).</summary>
    public List<PaneSession> Panes { get; set; } = [];

    /// <summary>The full docking layout tree (nested splits + panes) to restore (PRD §6.2).</summary>
    public FoileBrowser.Docking.DockNodeState? Tree { get; set; }
}

public sealed class PaneSession
{
    public List<string> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }
}
