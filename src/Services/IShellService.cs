namespace FoileBrowser.Services;

/// <summary>OS integration: open items with the default app and launch a terminal (PRD §6.9).</summary>
public interface IShellService
{
    /// <summary>Opens a file or folder with the platform's default handler.</summary>
    Task OpenAsync(string path);

    /// <summary>
    /// The terminal to launch for "Open terminal here" (PRD §6.9). Empty/null auto-detects the first
    /// installed terminal. Otherwise a command line, optionally containing <c>{dir}</c>, which is
    /// replaced by the folder; without it the folder becomes the process working directory.
    /// </summary>
    string? TerminalCommand { get; set; }

    /// <summary>Opens a terminal rooted at <paramref name="directory"/>.</summary>
    Task OpenTerminalAsync(string directory);
}
