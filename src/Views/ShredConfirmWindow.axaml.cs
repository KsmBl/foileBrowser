using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FoileBrowser.Views;

/// <summary>
/// Confirms an irreversible overwrite-and-delete (PRD §6.3). Returns true only if the user ticks the
/// acknowledgement — the destructive button stays disabled until then.
/// </summary>
public partial class ShredConfirmWindow : Window
{
    public ShredConfirmWindow() : this([])
    {
    }

    public ShredConfirmWindow(IReadOnlyList<string> paths)
    {
        InitializeComponent();

        TargetsText.Text = paths.Count switch
        {
            0 => "Nothing is selected.",
            1 => paths[0],
            <= 6 => string.Join("\n", paths),
            _ => string.Join("\n", paths.Take(5)) + $"\n… and {paths.Count - 5} more ({paths.Count} items in total)",
        };
    }

    private void OnConfirmChanged(object? sender, RoutedEventArgs e) =>
        ShredButton.IsEnabled = ConfirmBox.IsChecked == true;

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
