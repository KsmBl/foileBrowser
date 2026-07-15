using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is MainWindowViewModel vm)
            _ = vm.InitializeAsync();
    }

    // Opening is a view concern (double-click / Enter on the list); it forwards to the
    // view model's OpenCommand so folder descent stays in one place.
    private void OnEntryActivated(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedEntry is { } selected)
            vm.OpenCommand.Execute(selected);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnEntryActivated(sender, e);
            e.Handled = true;
        }
    }
}
