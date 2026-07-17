using CommunityToolkit.Mvvm.ComponentModel;

namespace FoileBrowser.ViewModels;

/// <summary>
/// Catalogue of the global operations-toolbar buttons that can be individually shown or hidden from
/// Settings (PRD §6.8). Each id matches the button's <c>ConverterParameter</c> in MainWindow.axaml.
/// </summary>
public static class ToolbarButtons
{
    public static readonly IReadOnlyList<(string Id, string Label)> All =
    [
        // Back/forward/up/refresh are not here — they live in each pane's own nav bar, not this toolbar.
        ("newFolder", "📁  New folder"),
        ("newFile", "📄  New file"),
        ("rename", "✏️  Rename"),
        ("delete", "🗑️  Delete to trash"),
        ("copyToOther", "📋  Copy to other pane"),
        ("moveToOther", "✂️  Move to other pane"),
        ("copyPath", "🔗  Copy path"),
        ("copyName", "🏷️  Copy name"),
        ("batchRename", "🔤  Batch rename"),
        ("terminal", "💻  Terminal here"),
        ("pin", "⭐  Pin folder"),
        ("newTab", "🗂️  New tab"),
        ("inspector", "🔍  Inspector"),
        ("sizeUnit", "Size units"),
        ("dateFormat", "Date format"),
        ("settings", "⚙️  Settings"),
    ];
}

/// <summary>A show/hide toggle for one toolbar button in the Settings dialog.</summary>
public sealed partial class ToolbarOption : ObservableObject
{
    public ToolbarOption(string id, string label, bool enabled)
    {
        Id = id;
        Label = label;
        _isEnabled = enabled;
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isEnabled;
}
