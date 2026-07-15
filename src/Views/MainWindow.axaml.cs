using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

public partial class MainWindow : Window
{
    /// <summary>A star column when dual-pane, collapsed to zero width in single-pane mode.</summary>
    public static readonly IValueConverter PaneColumnWidthConverter =
        new FuncValueConverter<bool, GridLength>(dual => dual ? new GridLength(1, GridUnitType.Star) : new GridLength(0));

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
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
            _ = vm.InitializeAsync();
    }

    private async void OnClipboardCopyRequested(object? sender, string text)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }
}
