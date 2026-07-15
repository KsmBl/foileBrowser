using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

public partial class PaneView : UserControl
{
    /// <summary>Accent border when the pane is active, transparent otherwise.</summary>
    public static readonly IValueConverter ActiveBorderConverter =
        new FuncValueConverter<bool, IBrush>(active =>
            active ? new SolidColorBrush(Color.FromRgb(0x3D, 0x8B, 0xFD)) : Brushes.Transparent);

    public PaneView()
    {
        InitializeComponent();
        // Any interaction inside the pane marks it as the active one (PRD §6.3).
        AddHandler(PointerPressedEvent, OnPanePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnPaneGotFocus, RoutingStrategies.Bubble);
    }

    private void OnPanePointerPressed(object? sender, PointerPressedEventArgs e) => Activate();

    private void OnPaneGotFocus(object? sender, GotFocusEventArgs e) => Activate();

    private void Activate()
    {
        if (DataContext is PaneViewModel { IsActive: false } vm)
            vm.ActivateCommand.Execute(null);
    }
}
