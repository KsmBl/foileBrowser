using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

public partial class FileTabView : UserControl
{
    public FileTabView()
    {
        InitializeComponent();
    }

    private void OnEntryActivated(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FileTabViewModel vm && vm.SelectedEntry is { } selected)
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
