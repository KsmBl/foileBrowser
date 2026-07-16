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

        // Any interaction inside this tab makes it the active tab, so operations (copy/move, inspector)
        // target it — Dock updates the active document on tab clicks, this covers content clicks too.
        AddHandler(PointerPressedEvent, (_, _) => Activate(), RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, (_, _) => Activate(), RoutingStrategies.Bubble);
    }

    private void Activate()
    {
        if (DataContext is FileTabViewModel tab
            && (TopLevel.GetTopLevel(this) as MainWindow)?.DataContext is MainWindowViewModel shell)
            shell.ActivateTab(tab);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // The entry context menu's commands live on the shell view-model (they act on the active tab).
        if ((TopLevel.GetTopLevel(this) as MainWindow)?.DataContext is MainWindowViewModel shell)
            EntryMenu.DataContext = shell;
    }

    // Right-clicking a row selects it first, so the context menu acts on the item under the cursor.
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Control).Properties.IsRightButtonPressed)
            return;
        if ((e.Source as Control)?.DataContext is FileEntryViewModel entry && DataContext is FileTabViewModel tab)
            tab.SelectedEntry = entry;
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
