using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

public partial class MainWindow : Window
{
    /// <summary>A star column when dual-pane, collapsed to zero width in single-pane mode.</summary>
    public static readonly IValueConverter PaneColumnWidthConverter =
        new FuncValueConverter<bool, GridLength>(dual => dual ? new GridLength(1, GridUnitType.Star) : new GridLength(0));

    /// <summary>True when the bound value is non-null/non-empty (used to hide empty hotkey chips).</summary>
    public static readonly IValueConverter NotNullConverter =
        new FuncValueConverter<object?, bool>(v => v is string s ? !string.IsNullOrEmpty(s) : v is not null);

    /// <summary>True when the bound value is null (used for the empty-inspector placeholder).</summary>
    public static readonly IValueConverter IsNullConverter =
        new FuncValueConverter<object?, bool>(v => v is null);

    private CommandPaletteViewModel? _palette;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm)
        {
            // The view supplies the rename prompt and clipboard access the VM asks for.
            vm.NameRequester = current => new NameInputWindow(current).ShowDialog<string?>(this);
            vm.ClipboardCopyRequested -= OnClipboardCopyRequested;
            vm.ClipboardCopyRequested += OnClipboardCopyRequested;

            if (_palette is not null)
                _palette.PropertyChanged -= OnPalettePropertyChanged;
            _palette = vm.CommandPalette;
            _palette.PropertyChanged += OnPalettePropertyChanged;
        }
    }

    // Focus the palette query box the moment it opens.
    private void OnPalettePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && _palette is { IsOpen: true })
            Dispatcher.UIThread.Post(() =>
            {
                PaletteQueryBox.Focus();
                PaletteQueryBox.SelectAll();
            });
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_palette is null)
            return;

        switch (e.Key)
        {
            case Key.Down:
                _palette.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                _palette.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                _palette.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                _palette.Close();
                e.Handled = true;
                break;
        }
    }

    private void OnPaletteItemActivated(object? sender, RoutedEventArgs e) =>
        _palette?.ExecuteSelectedCommand.Execute(null);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
            _ = vm.InitializeAsync();
    }

    /// <summary>Opens the spacebar quick-preview popup for the current inspector preview (PRD §6.5).</summary>
    public void ShowQuickPreview()
    {
        if (DataContext is MainWindowViewModel { Preview: { } preview })
            new QuickPreviewWindow(preview).Show(this);
    }

    private async void OnClipboardCopyRequested(object? sender, string text)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }
}
