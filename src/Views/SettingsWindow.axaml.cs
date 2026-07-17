using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FoileBrowser.Models;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

/// <summary>Edits scalar settings and hotkeys. Returns true if the user saved (PRD §6.8, §6.6).</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private KeybindRow? _capturing;

    /// <summary>Editable copy of the rebindable hotkeys; committed to settings on Save.</summary>
    public ObservableCollection<KeybindRow> Keybinds { get; } = [];

    public SettingsWindow() : this(new AppSettings(), [])
    {
    }

    public SettingsWindow(AppSettings settings, IReadOnlyList<CommandItem> rebindable)
    {
        InitializeComponent();
        DataContext = this;
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

        foreach (var command in rebindable)
            Keybinds.Add(new KeybindRow(command.Id, command.Title, command.DefaultGesture, command.Gesture));
        RecomputeConflicts();
    }

    // ---- keybind capture (PRD §6.6) ----

    private void OnCaptureClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is not KeybindRow row)
            return;
        // Toggle: a second click cancels; starting one cancels any other in-flight capture.
        if (ReferenceEquals(_capturing, row))
        {
            row.IsCapturing = false;
            _capturing = null;
            return;
        }
        if (_capturing is not null)
            _capturing.IsCapturing = false;
        row.IsCapturing = true;
        _capturing = row;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_capturing is null)
        {
            base.OnKeyDown(e);
            return;
        }

        // Wait for a non-modifier key; Escape cancels the capture without binding.
        if (IsModifierKey(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            _capturing.IsCapturing = false;
            _capturing = null;
            e.Handled = true;
            return;
        }

        _capturing.Gesture = new KeyGesture(e.Key, e.KeyModifiers).ToString();
        _capturing.IsCapturing = false;
        _capturing = null;
        RecomputeConflicts();
        e.Handled = true;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    private void OnResetKeybind(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is KeybindRow row)
        {
            row.Gesture = row.DefaultGesture;
            RecomputeConflicts();
        }
    }

    private void OnClearKeybind(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is KeybindRow row)
        {
            row.Gesture = null;
            RecomputeConflicts();
        }
    }

    private void OnResetAllKeybinds(object? sender, RoutedEventArgs e)
    {
        foreach (var row in Keybinds)
            row.Gesture = row.DefaultGesture;
        RecomputeConflicts();
    }

    /// <summary>Flags any two rows that share the same gesture so the user resolves them before saving.</summary>
    private void RecomputeConflicts()
    {
        foreach (var row in Keybinds)
        {
            var g = row.Gesture?.Trim();
            row.Conflict = string.IsNullOrEmpty(g)
                ? null
                : Keybinds.FirstOrDefault(o => !ReferenceEquals(o, row)
                        && string.Equals(o.Gesture?.Trim(), g, System.StringComparison.OrdinalIgnoreCase)) is { } clash
                    ? $"conflicts with “{clash.Title}”"
                    : null;
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Keybinds.Any(r => r.HasConflict))
        {
            StatusText.Text = "Resolve keybind conflicts first.";
            return;
        }

        _settings.ThemeVariant = ThemeBox.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        if (!string.IsNullOrWhiteSpace(AccentBox.Text))
            _settings.AccentColor = AccentBox.Text.Trim();
        if (FontBox.Value is { } f)
            _settings.FontSize = (double)f;
        if (RowHeightBox.Value is { } r)
            _settings.RowHeight = (double)r;

        // Persist only overrides: a gesture equal to the default is dropped (so it tracks default
        // changes); an empty gesture is stored explicitly to mean "unbound".
        foreach (var row in Keybinds)
        {
            var g = row.Gesture?.Trim() ?? string.Empty;
            var def = row.DefaultGesture?.Trim() ?? string.Empty;
            if (string.Equals(g, def, System.StringComparison.OrdinalIgnoreCase))
                _settings.Keybinds.Remove(row.Id);
            else
                _settings.Keybinds[row.Id] = g;
        }

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
