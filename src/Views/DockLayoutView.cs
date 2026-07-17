using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using FoileBrowser.Docking;

namespace FoileBrowser.Views;

/// <summary>
/// Renders a <see cref="DockLayout"/> and drives its drag interactions — the Avalonia front-end for the
/// toolkit-agnostic docking model (PRD §6.2). Splits become nested Grids with <see cref="GridSplitter"/>s;
/// each pane is a tab strip over its active tab's content. Dragging a tab reorders it in its strip,
/// moves it onto another pane's strip, or (dropped over a pane edge) splits that pane.
/// </summary>
public sealed class DockLayoutView : Grid
{
    public static readonly StyledProperty<DockLayout?> LayoutProperty =
        AvaloniaProperty.Register<DockLayoutView, DockLayout?>(nameof(Layout));

    public DockLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    private readonly ContentControl _host = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Border _highlight = new()
    {
        IsVisible = false,
        Background = new SolidColorBrush(Color.FromArgb(60, 45, 125, 245)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(200, 45, 125, 245)),
        BorderThickness = new Thickness(2),
    };

    // Live subscriptions from the current render; cleared and re-made on each full rebuild.
    private readonly List<Action> _cleanup = [];

    // Drag state.
    private IDockable? _dragTab;
    private Point _dragStart;
    private bool _dragging;
    private DockPane? _dropPane;
    private DockSide _dropSide;
    private int _dropIndex;

    public DockLayoutView()
    {
        _overlay.Children.Add(_highlight);
        Children.Add(_host);
        Children.Add(_overlay);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LayoutProperty)
        {
            if (change.OldValue is DockLayout oldLayout)
                oldLayout.StructureChanged -= OnStructureChanged;
            if (change.NewValue is DockLayout newLayout)
                newLayout.StructureChanged += OnStructureChanged;
            Rebuild();
        }
    }

    private void OnStructureChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        foreach (var undo in _cleanup)
            undo();
        _cleanup.Clear();

        _host.Content = Layout is null ? null : BuildNode(Layout.Root);
    }

    // ---- rendering ----

    private Control BuildNode(DockNode node) =>
        node is DockSplit split ? BuildSplit(split) : BuildPane((DockPane)node);

    private Control BuildSplit(DockSplit split)
    {
        var grid = new Grid();
        var horizontal = split.Orientation == DockOrientation.Horizontal;

        for (var i = 0; i < split.Children.Count; i++)
        {
            if (i > 0)
                AddTrack(grid, horizontal, GridLength.Auto); // splitter track
            AddTrack(grid, horizontal, new GridLength(Math.Max(0.05, split.Children[i].Weight), GridUnitType.Star));
        }

        for (var i = 0; i < split.Children.Count; i++)
        {
            var trackIndex = i * 2; // account for splitter tracks
            if (i > 0)
            {
                var splitter = new GridSplitter
                {
                    Background = Brushes.Transparent,
                    ResizeDirection = horizontal ? GridResizeDirection.Columns : GridResizeDirection.Rows,
                };
                if (horizontal) { splitter.Width = 5; splitter.Cursor = new Cursor(StandardCursorType.SizeWestEast); }
                else { splitter.Height = 5; splitter.Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }
                Place(splitter, horizontal, trackIndex - 1);
                splitter.AddHandler(PointerReleasedEvent, (_, _) => SyncWeights(grid, split, horizontal), RoutingStrategies.Bubble);
                grid.Children.Add(splitter);
            }

            var childControl = BuildNode(split.Children[i]);
            Place(childControl, horizontal, trackIndex);
            grid.Children.Add(childControl);
        }

        return grid;
    }

    private static void AddTrack(Grid grid, bool horizontal, GridLength length)
    {
        if (horizontal)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = length });
        else
            grid.RowDefinitions.Add(new RowDefinition { Height = length });
    }

    private static void Place(Control control, bool horizontal, int index)
    {
        if (horizontal)
            Grid.SetColumn(control, index);
        else
            Grid.SetRow(control, index);
    }

    /// <summary>Reads the live splitter-adjusted star sizes back into the model weights so they persist.</summary>
    private static void SyncWeights(Grid grid, DockSplit split, bool horizontal)
    {
        for (var i = 0; i < split.Children.Count; i++)
        {
            var track = i * 2;
            var value = horizontal ? grid.ColumnDefinitions[track].Width.Value : grid.RowDefinitions[track].Height.Value;
            if (value > 0)
                split.Children[i].Weight = value;
        }
    }

    private Control BuildPane(DockPane pane)
    {
        var grid = new Grid { Tag = pane };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(2) };
        Grid.SetRow(strip, 0);

        var content = new ContentControl { Content = pane.ActiveTab };
        Grid.SetRow(content, 1);

        // Subscriptions scoped to the current strip contents; cleared whenever the strip is rebuilt
        // (kept trim/AOT-safe by updating properties directly instead of using reflection bindings).
        var stripCleanup = new List<Action>();

        void RebuildStrip()
        {
            foreach (var undo in stripCleanup)
                undo();
            stripCleanup.Clear();
            strip.Children.Clear();

            // A lone tab in a single-pane layout hides the strip; docking contexts keep it (to grab/close tabs).
            var show = pane.Tabs.Count > 1 || (Layout is { } l && l.Panes().Count() > 1);
            strip.IsVisible = show;
            if (!show)
                return;
            foreach (var tab in pane.Tabs)
                strip.Children.Add(BuildTabHeader(pane, tab, stripCleanup));
        }

        RebuildStrip();
        void OnTabsChanged(object? s, NotifyCollectionChangedEventArgs e) => RebuildStrip();
        void OnActiveChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DockPane.ActiveTab))
                return;
            content.Content = pane.ActiveTab;
            RebuildStrip();
        }
        pane.Tabs.CollectionChanged += OnTabsChanged;
        pane.PropertyChanged += OnActiveChanged;
        _cleanup.Add(() => pane.Tabs.CollectionChanged -= OnTabsChanged);
        _cleanup.Add(() => pane.PropertyChanged -= OnActiveChanged);
        _cleanup.Add(() => { foreach (var undo in stripCleanup) undo(); stripCleanup.Clear(); });

        grid.Children.Add(strip);
        grid.Children.Add(content);
        return grid;
    }

    private Control BuildTabHeader(DockPane pane, IDockable tab, List<Action> stripCleanup)
    {
        var active = ReferenceEquals(pane.ActiveTab, tab);
        var title = new TextBlock
        {
            Text = tab.Title,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 3, 4, 3),
            FontSize = 12,
        };
        // Follow renames without a reflection binding.
        void OnTitleChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IDockable.Title))
                title.Text = tab.Title;
        }
        tab.PropertyChanged += OnTitleChanged;
        stripCleanup.Add(() => tab.PropertyChanged -= OnTitleChanged);

        var close = new Button
        {
            Content = "✕",
            FontSize = 10,
            Padding = new Thickness(4, 0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        close.Click += (_, _) => Layout?.CloseTab(tab);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(title);
        row.Children.Add(close);

        var header = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(4, 4, 0, 0),
            Background = active
                ? new SolidColorBrush(Color.FromArgb(40, 128, 128, 128))
                : Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            BorderThickness = new Thickness(1, 1, 1, 0),
            Tag = tab,
        };

        header.PointerPressed += (_, e) =>
        {
            Layout?.Activate(tab);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _dragTab = tab;
                _dragStart = e.GetPosition(this);
            }
        };
        return header;
    }

    // ---- tab dragging (pointer-based; all within this control) ----

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragTab is null)
            return;

        var pos = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(pos.X - _dragStart.X) < 6 && Math.Abs(pos.Y - _dragStart.Y) < 6)
                return;
            _dragging = true;
            e.Pointer.Capture(this);
        }

        UpdateDropTarget(pos);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging && _dragTab is { } tab && _dropPane is { } pane)
        {
            if (_dropSide == DockSide.Center)
                Layout?.MoveTab(tab, pane, _dropIndex);
            else
                Layout?.Split(tab, pane, _dropSide);
        }

        _dragTab = null;
        _dragging = false;
        _dropPane = null;
        _highlight.IsVisible = false;
        e.Pointer.Capture(null);
    }

    private void UpdateDropTarget(Point pos)
    {
        var paneGrid = PaneControlAt(pos);
        if (paneGrid?.Tag is not DockPane pane)
        {
            _dropPane = null;
            _highlight.IsVisible = false;
            return;
        }
        _dropPane = pane;

        var origin = paneGrid.TranslatePoint(default, this) ?? default;
        var bounds = new Rect(origin, paneGrid.Bounds.Size);
        var strip = (paneGrid as Grid)?.Children.OfType<StackPanel>().FirstOrDefault();

        // Over the (visible) tab strip → move/reorder into this pane at the hovered index.
        if (strip is { IsVisible: true } && strip.TranslatePoint(default, this) is { } stripOrigin
            && new Rect(stripOrigin, strip.Bounds.Size).Contains(pos))
        {
            _dropSide = DockSide.Center;
            _dropIndex = InsertIndex(strip, stripOrigin, pos);
            ShowHighlight(new Rect(stripOrigin, strip.Bounds.Size));
            return;
        }

        // Otherwise the pane body: edges split, the middle moves in (as a tab).
        var rx = bounds.Width > 0 ? (pos.X - bounds.X) / bounds.Width : 0.5;
        var ry = bounds.Height > 0 ? (pos.Y - bounds.Y) / bounds.Height : 0.5;
        (_dropSide, var zone) = rx < 0.25 ? (DockSide.Left, LeftHalf(bounds))
            : rx > 0.75 ? (DockSide.Right, RightHalf(bounds))
            : ry < 0.25 ? (DockSide.Top, TopHalf(bounds))
            : ry > 0.75 ? (DockSide.Bottom, BottomHalf(bounds))
            : (DockSide.Center, bounds);
        _dropIndex = pane.Tabs.Count;
        ShowHighlight(zone);
    }

    private Grid? PaneControlAt(Point pos)
    {
        var hit = this.InputHitTest(pos) as Visual;
        while (hit is not null)
        {
            if (hit is Grid g && g.Tag is DockPane)
                return g;
            hit = hit.GetVisualParent();
        }
        return null;
    }

    private static int InsertIndex(StackPanel strip, Point stripOrigin, Point posInView)
    {
        var localX = posInView.X - stripOrigin.X; // pointer in strip-local coordinates
        var index = 0;
        foreach (var child in strip.Children.OfType<Control>())
        {
            if (localX < child.Bounds.X + child.Bounds.Width / 2)
                break;
            index++;
        }
        return index;
    }

    private void ShowHighlight(Rect rect)
    {
        Canvas.SetLeft(_highlight, rect.X);
        Canvas.SetTop(_highlight, rect.Y);
        _highlight.Width = rect.Width;
        _highlight.Height = rect.Height;
        _highlight.IsVisible = true;
    }

    private static Rect LeftHalf(Rect b) => new(b.X, b.Y, b.Width / 2, b.Height);
    private static Rect RightHalf(Rect b) => new(b.X + b.Width / 2, b.Y, b.Width / 2, b.Height);
    private static Rect TopHalf(Rect b) => new(b.X, b.Y, b.Width, b.Height / 2);
    private static Rect BottomHalf(Rect b) => new(b.X, b.Y + b.Height / 2, b.Width, b.Height / 2);
}
