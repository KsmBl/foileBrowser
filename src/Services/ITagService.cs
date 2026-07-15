namespace FoileBrowser.Services;

/// <summary>A named color tag that can be applied to files/folders (PRD §6.7).</summary>
public sealed record TagColor(string Name, string Hex);

/// <summary>
/// Assigns and persists color tags per path (PRD §6.7). Backed by the settings file so tags
/// survive restarts.
/// </summary>
public interface ITagService
{
    /// <summary>The fixed set of assignable tag colors.</summary>
    IReadOnlyList<TagColor> Palette { get; }

    /// <summary>Returns the tag hex for <paramref name="path"/>, or null if untagged.</summary>
    string? GetTag(string path);

    /// <summary>Sets (or, when <paramref name="hex"/> is null, clears) the tag and persists.</summary>
    Task SetTagAsync(string path, string? hex);
}
