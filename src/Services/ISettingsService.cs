using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>Loads and persists <see cref="AppSettings"/> as portable JSON (PRD §6.8).</summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Reads settings from disk, falling back to defaults on missing/corrupt files.</summary>
    Task LoadAsync();

    /// <summary>Writes the current settings to disk.</summary>
    Task SaveAsync();

    /// <summary>Absolute path of the settings file.</summary>
    string FilePath { get; }
}
