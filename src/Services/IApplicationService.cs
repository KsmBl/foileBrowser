namespace FoileBrowser.Services;

/// <summary>An installed application that can open a file (PRD §6.9).</summary>
/// <param name="Id">Platform handle — a <c>.desktop</c> file id on Linux, an executable path elsewhere.</param>
/// <param name="Name">Human-readable name shown in the menu.</param>
public sealed record DesktopApp(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// "Open with" and default-application association for a file's type (PRD §6.9). Fully implemented on
/// Linux via the XDG mime database; other platforms fall back to their own shell handlers.
/// </summary>
public interface IApplicationService
{
    /// <summary>True when this platform can enumerate and change associations, not just open a file.</summary>
    bool SupportsAssociations { get; }

    /// <summary>The file's type identifier (a MIME type on Linux), or empty if it can't be determined.</summary>
    Task<string> GetTypeAsync(string path);

    /// <summary>Applications registered as able to open this file, best match first. May be empty.</summary>
    Task<IReadOnlyList<DesktopApp>> GetCandidatesAsync(string path);

    /// <summary>The application currently registered as the default for this file's type, if any.</summary>
    Task<DesktopApp?> GetDefaultAsync(string path);

    /// <summary>Makes <paramref name="app"/> the default for this file's type.</summary>
    Task SetDefaultAsync(string path, DesktopApp app);

    /// <summary>Opens <paramref name="path"/> with a specific application.</summary>
    Task LaunchAsync(DesktopApp app, string path);
}
