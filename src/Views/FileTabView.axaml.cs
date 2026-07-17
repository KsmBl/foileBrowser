using System.Linq;
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

    private FileTabViewModel? _boundTab;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // Follow the search-focus request (Ctrl+F) for whichever tab this view now renders.
        if (_boundTab is not null)
            _boundTab.SearchFocusRequested -= OnSearchFocusRequested;
        _boundTab = DataContext as FileTabViewModel;
        if (_boundTab is not null)
            _boundTab.SearchFocusRequested += OnSearchFocusRequested;
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        });

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // The entry context menu's commands live on the shell view-model (they act on the active tab).
        if ((TopLevel.GetTopLevel(this) as MainWindow)?.DataContext is MainWindowViewModel shell)
            EntryMenu.DataContext = shell;
    }

    // ---- searchable context menu (PRD §6.6) ----

    private void OnEntryMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        MenuSearchBox.Text = string.Empty;
        FilterMenu(string.Empty);
        // Focus the search box once the popup is up so the user can type immediately.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => MenuSearchBox.Focus());
    }

    private void OnMenuSearchChanged(object? sender, TextChangedEventArgs e) => FilterMenu(MenuSearchBox.Text ?? "");

    private void FilterMenu(string query)
    {
        var searching = !string.IsNullOrWhiteSpace(query);
        foreach (var item in EntryMenu.Items)
        {
            switch (item)
            {
                case MenuItem mi:
                    mi.IsVisible = !searching || FoileBrowser.Services.FuzzyMatcher.IsMatch(query, HeaderText(mi));
                    break;
                case Separator sep:
                    sep.IsVisible = !searching; // separators only clutter a filtered list
                    break;
            }
        }
    }

    private void OnMenuSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Run the first matching, enabled action.
                var first = EntryMenu.Items.OfType<MenuItem>()
                    .FirstOrDefault(m => m.IsVisible && m.IsEffectivelyEnabled && m.Command is not null);
                if (first is not null)
                {
                    first.Command!.Execute(first.CommandParameter);
                    EntryMenu.Close();
                }
                e.Handled = true;
                break;
            case Key.Down:
                EntryMenu.Items.OfType<MenuItem>().FirstOrDefault(m => m.IsVisible)?.Focus();
                e.Handled = true;
                break;
            case Key.Escape:
                EntryMenu.Close();
                e.Handled = true;
                break;
        }
    }

    private static string HeaderText(MenuItem item) => item.Header?.ToString() ?? string.Empty;

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
