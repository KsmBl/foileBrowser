namespace FoileBrowser.ViewModels;

/// <summary>
/// A named, runnable action surfaced in the command palette (PRD §6.6). The gesture is the
/// display hint for its hotkey and is rebindable in principle (persisted rebinding is a later
/// milestone).
/// </summary>
public sealed class CommandItem
{
    private readonly Func<Task> _run;

    public CommandItem(string id, string title, string category, string? gesture, Func<Task> run)
    {
        Id = id;
        Title = title;
        Category = category;
        Gesture = gesture;
        _run = run;
    }

    public string Id { get; }
    public string Title { get; }
    public string Category { get; }
    public string? Gesture { get; }

    public string DisplayCategory => $"{Category}";

    public Task ExecuteAsync() => _run();
}
