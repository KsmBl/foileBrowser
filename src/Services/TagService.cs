namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class TagService : ITagService
{
    private readonly ISettingsService _settings;

    public TagService(ISettingsService settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<TagColor> Palette { get; } =
    [
        new("Red", "#E5484D"),
        new("Orange", "#F76B15"),
        new("Yellow", "#FFB224"),
        new("Green", "#46A758"),
        new("Blue", "#3D8BFD"),
        new("Purple", "#8E4EC6"),
    ];

    public string? GetTag(string path) =>
        _settings.Current.Tags.TryGetValue(path, out var hex) ? hex : null;

    public Task SetTagAsync(string path, string? hex)
    {
        if (string.IsNullOrEmpty(hex))
            _settings.Current.Tags.Remove(path);
        else
            _settings.Current.Tags[path] = hex;

        return _settings.SaveAsync();
    }
}
