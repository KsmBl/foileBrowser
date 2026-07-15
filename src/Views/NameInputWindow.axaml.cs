using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace FoileBrowser.Views;

/// <summary>Minimal modal name prompt returning the entered string, or null if cancelled.</summary>
public partial class NameInputWindow : Window
{
    public NameInputWindow() : this(string.Empty)
    {
    }

    public NameInputWindow(string initial)
    {
        InitializeComponent();
        NameBox.Text = initial;
        Opened += (_, _) =>
        {
            NameBox.SelectAll();
            NameBox.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(null);
        };
    }

    private void OnOk(object? sender, RoutedEventArgs e) =>
        Close(string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim());

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
