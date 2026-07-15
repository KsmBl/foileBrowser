namespace FoileBrowser.Services;

/// <summary>OS integration: open items with the default app and launch a terminal (PRD §6.9).</summary>
public interface IShellService
{
    /// <summary>Opens a file or folder with the platform's default handler.</summary>
    Task OpenAsync(string path);

    /// <summary>Opens a terminal rooted at <paramref name="directory"/>.</summary>
    Task OpenTerminalAsync(string directory);
}
