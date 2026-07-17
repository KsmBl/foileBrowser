using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FoileBrowser.ViewModels;

/// <summary>
/// One button on the global operations toolbar, as a data item so the toolbar can be reordered by
/// drag-and-drop and individual buttons hidden (PRD §6.8). <see cref="Content"/> is observable because
/// the size/date buttons show live labels.
/// </summary>
public sealed partial class ToolbarItemViewModel : ObservableObject
{
    public required string Id { get; init; }
    public required string Tooltip { get; init; }
    public required ICommand Command { get; init; }
    public double FontSize { get; init; } = 15;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;
}
