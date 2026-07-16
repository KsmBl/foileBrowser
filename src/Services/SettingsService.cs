using System.Text.Json;
using System.Text.Json.Serialization;
using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>Source-generated JSON context so settings (de)serialize without reflection (trim/AOT-safe).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <inheritdoc />
public sealed class SettingsService : ISettingsService
{

    public SettingsService(string? filePath = null)
    {
        FilePath = filePath ?? DefaultPath();
    }

    public AppSettings Current { get; private set; } = new();

    public string FilePath { get; }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Current = new AppSettings();
                return;
            }

            await using var stream = File.OpenRead(FilePath);
            Current = await JsonSerializer.DeserializeAsync(stream, SettingsJsonContext.Default.AppSettings)
                      ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Corrupt or unreadable settings should never block startup.
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Write to a temp file then move, so a crash mid-write can't corrupt the settings.
            var tmp = FilePath + ".tmp";
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, Current, SettingsJsonContext.Default.AppSettings);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence; ignore failures (e.g. read-only location).
        }
    }

    private static string DefaultPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "foileBrowser", "settings.json");
    }
}
