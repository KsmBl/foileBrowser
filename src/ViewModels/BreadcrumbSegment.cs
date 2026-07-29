namespace FoileBrowser.ViewModels;

/// <summary>What a breadcrumb points at, which decides what clicking it does (PRD §6.1/§6.11).</summary>
public enum BreadcrumbKind
{
    /// <summary>A real directory. Clicking it lists that folder — leaving an archive if one is open.</summary>
    Folder,

    /// <summary>The archive file being browsed. Clicking it returns to the archive's root.</summary>
    Archive,

    /// <summary>A directory inside the open archive.</summary>
    ArchiveEntry,
}

/// <summary>
/// One clickable segment of the breadcrumb path bar (PRD §6.1).
/// </summary>
/// <remarks>
/// <see cref="Path"/> is the full path to the thing, including for entries inside an archive. The
/// bar joins the segment names back into a path to seed its editable field, so a trail that stopped
/// at the archive file would offer "sample.zip/sub" — which is not a path anyone can navigate to.
/// </remarks>
public sealed record BreadcrumbSegment(
    string Name,
    string Path,
    bool ShowSeparator = false,
    BreadcrumbKind Kind = BreadcrumbKind.Folder)
{
    /// <summary>
    /// What joins these segments back into a path. The toolkit's bar defaults to "/" and treats the
    /// separator as a text convention, so that an archive or any other virtual namespace can pick its
    /// own — but every <see cref="Path"/> here is a real filesystem path, archive entries included, so
    /// this trail composes with the platform's separator. Left at the default, a Windows root crumb of
    /// "C:\" gained a second, foreign separator and the path bar opened on "C:\/Users".
    /// </summary>
    public static string Separator { get; } = System.IO.Path.DirectorySeparatorChar.ToString();
}
