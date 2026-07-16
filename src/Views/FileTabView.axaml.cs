using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

public partial class FileTabView : UserControl
{
    public FileTabView()
    {
        InitializeComponent();
        // Whenever the path entry becomes visible (via click or Ctrl+L), focus and select it.
        PathEntry.GetObservable(Visual.IsVisibleProperty).Subscribe(new AnonymousObserver<bool>(visible =>
        {
            if (visible)
            {
                PathEntry.Focus();
                PathEntry.SelectAll();
            }
        }));
    }

    // Clicking the empty area of the breadcrumb bar switches it to an editable path entry (Thunar-style).
    private void OnPathBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is FileTabViewModel { IsEditingPath: false } vm)
            vm.BeginEditPathCommand.Execute(null);
    }

    // Leaving the entry without committing reverts to the breadcrumb view.
    private void OnPathEntryLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FileTabViewModel { IsEditingPath: true } vm)
            vm.CancelEditPathCommand.Execute(null);
    }

    private void OnEntryActivated(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FileTabViewModel vm && vm.SelectedEntry is { } selected)
            vm.OpenCommand.Execute(selected);
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        var shell = (TopLevel.GetTopLevel(this) as MainWindow)?.DataContext as MainWindowViewModel;
        switch (e.Key)
        {
            case Key.Enter:
                OnEntryActivated(sender, e);
                e.Handled = true;
                break;
            case Key.Space:
                // Spacebar quick-preview popup (PRD §6.5).
                (TopLevel.GetTopLevel(this) as MainWindow)?.ShowQuickPreview();
                e.Handled = true;
                break;
            // Delete/F2 live here (not as window shortcuts) so they only act on files when the
            // list has focus, never while typing in a text box.
            case Key.Delete:
                shell?.DeleteSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F2:
                shell?.RenameSelectedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
