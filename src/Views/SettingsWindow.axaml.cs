using Avalonia.Controls;
using Avalonia.Interactivity;
using FoileBrowser.Models;

namespace FoileBrowser.Views;

/// <summary>Edits scalar settings. Returns true if the user saved (PRD §6.8).</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow() : this(new AppSettings())
    {
    }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        ThemeBox.SelectedIndex = settings.ThemeVariant switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };
        AccentBox.Text = settings.AccentColor;
        FontBox.Value = (decimal)settings.FontSize;
        RowHeightBox.Value = (decimal)settings.RowHeight;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _settings.ThemeVariant = ThemeBox.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        if (!string.IsNullOrWhiteSpace(AccentBox.Text))
            _settings.AccentColor = AccentBox.Text.Trim();
        if (FontBox.Value is { } f)
            _settings.FontSize = (double)f;
        if (RowHeightBox.Value is { } r)
            _settings.RowHeight = (double)r;

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
