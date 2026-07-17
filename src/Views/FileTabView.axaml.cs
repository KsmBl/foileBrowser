using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Reactive;
using Avalonia.VisualTree;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

public partial class FileTabView : UserControl
{
    private readonly DragReorder _sectionDrag = new();

    public FileTabView()
    {
        InitializeComponent();
        SidebarSectionsHost.AddHandler(DragDrop.DragOverEvent, OnSectionDragOver);
        SidebarSectionsHost.AddHandler(DragDrop.DropEvent, OnSectionDrop);
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

    // Escape from the search box collapses a revealed-on-demand bar and returns focus to the file list.
    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        if ((TopLevel.GetTopLevel(this) as MainWindow)?.DataContext is MainWindowViewModel shell)
            shell.CollapseSearchBar();
        FileList.Focus();
        e.Handled = true;
    }

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

    // Pushes the full multi-selection to the view model (for the status summary and batch operations).
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && DataContext is FileTabViewModel tab)
            tab.SetSelection(list.SelectedItems?.OfType<FileEntryViewModel>().ToList() ?? []);
    }

    // Right-clicking keeps an existing multi-selection (so the menu acts on all of it); otherwise it
    // selects just the row under the cursor. Left-clicking the empty area below the list deselects.
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var list = sender as ListBox;
        var props = e.GetCurrentPoint(list).Properties;
        var row = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);

        if (props.IsRightButtonPressed)
        {
            if (row?.DataContext is FileEntryViewModel entry && list?.SelectedItems is { } sel && !sel.Contains(entry))
            {
                sel.Clear();
                sel.Add(entry);
            }
            return;
        }

        // Left-press on empty space starts a rubber-band selection (and clears the current one).
        if (props.IsLeftButtonPressed && row is null && list is not null)
        {
            list.SelectedItems?.Clear();
            _marqueeStart = e.GetPosition(FileList);
            _marqueeActive = true;
            Marquee.IsVisible = false;
            e.Pointer.Capture(FileList);
        }
    }

    // ---- rubber-band (marquee) selection (PRD §6.1) ----

    private bool _marqueeActive;
    private Avalonia.Point _marqueeStart;

    private void OnListPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_marqueeActive)
            return;

        var cur = e.GetPosition(FileList);
        var x = Math.Min(_marqueeStart.X, cur.X);
        var y = Math.Min(_marqueeStart.Y, cur.Y);
        var w = Math.Abs(cur.X - _marqueeStart.X);
        var h = Math.Abs(cur.Y - _marqueeStart.Y);
        var box = new Avalonia.Rect(x, y, w, h);

        Canvas.SetLeft(Marquee, x);
        Canvas.SetTop(Marquee, y);
        Marquee.Width = w;
        Marquee.Height = h;
        Marquee.IsVisible = w > 2 || h > 2;

        // Select every realized row whose bounds intersect the marquee (virtualized rows off-screen
        // aren't hit-tested, which is fine — you can only drag over what's visible).
        foreach (var container in FileList.GetRealizedContainers())
        {
            if (container is not ListBoxItem item)
                continue;
            if (item.TranslatePoint(default, FileList) is not { } topLeft)
                continue;
            item.IsSelected = box.Intersects(new Avalonia.Rect(topLeft, item.Bounds.Size));
        }
        e.Handled = true;
    }

    private void OnListPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_marqueeActive)
            return;
        _marqueeActive = false;
        Marquee.IsVisible = false;
        e.Pointer.Capture(null);
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

    // Selecting a folder in the tree navigates this pane there (the tree is per-pane).
    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TreeView { SelectedItem: FolderNodeViewModel { Path.Length: > 0 } node }
            && DataContext is FileTabViewModel tab)
            _ = tab.NavigateToAsync(node.Path);
    }

    // ---- sidebar section drag-reorder (PRD §6.2) ----

    private void OnSectionHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SidebarSectionViewModel section)
            _sectionDrag.Arm(section.Id, e, this);
    }

    private async void OnSectionHeaderMoved(object? sender, PointerEventArgs e) =>
        await _sectionDrag.MaybeStartAsync(e, this);

    private static void OnSectionDragOver(object? sender, DragEventArgs e) => DragReorder.Accept(e);

    private void OnSectionDrop(object? sender, DragEventArgs e)
    {
        var shell = (TopLevel.GetTopLevel(this) as MainWindow)?.DataContext as MainWindowViewModel;
        if (shell is not null && DragReorder.DroppedId(e) is { } fromId && SectionAt(e.Source) is { } target)
            shell.MoveSidebarSection(fromId, target.Id);
        e.Handled = true;
    }

    /// <summary>Walks up from the drop source to the sidebar section it landed on.</summary>
    private static SidebarSectionViewModel? SectionAt(object? source)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
            if ((v as Control)?.DataContext is SidebarSectionViewModel section)
                return section;
        return null;
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        var shell = (TopLevel.GetTopLevel(this) as MainWindow)?.DataContext as MainWindowViewModel;
        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                OnEntryActivated(sender, e);
                e.Handled = true;
                break;
            // Alt+Enter is left unhandled so it reaches the window's Properties shortcut.
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
