using CommunityToolkit.Mvvm.ComponentModel;

namespace FoileBrowser.ViewModels;

/// <summary>
/// One editable row in the keybind editor (PRD §6.6): a command's title and its working gesture,
/// captured live by pressing keys. Edits stay local until the dialog is saved.
/// </summary>
public sealed partial class KeybindRow : ObservableObject
{
    public KeybindRow(string id, string title, string? defaultGesture, string? gesture)
    {
        Id = id;
        Title = title;
        DefaultGesture = defaultGesture;
        _gesture = gesture;
    }

    public string Id { get; }
    public string Title { get; }
    public string? DefaultGesture { get; }

    /// <summary>The working gesture (null/empty = unbound). Shown in the row and written on Save.</summary>
    [ObservableProperty]
    private string? _gesture;

    /// <summary>True while this row is waiting to capture the next keystroke.</summary>
    [ObservableProperty]
    private bool _isCapturing;

    /// <summary>Non-empty when this row's gesture clashes with another command's.</summary>
    [ObservableProperty]
    private string? _conflict;

    /// <summary>Placeholder shown while capturing, else the current gesture (or "—" when unbound).</summary>
    public string Display => IsCapturing ? "Press keys…" : string.IsNullOrWhiteSpace(Gesture) ? "—" : Gesture!;

    public bool HasConflict => !string.IsNullOrEmpty(Conflict);

    partial void OnGestureChanged(string? value) => OnPropertyChanged(nameof(Display));
    partial void OnIsCapturingChanged(bool value) => OnPropertyChanged(nameof(Display));
    partial void OnConflictChanged(string? value) => OnPropertyChanged(nameof(HasConflict));
}
