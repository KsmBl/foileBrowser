using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;
using FoileBrowser.Models;
using FoileBrowser.ViewModels;

namespace FoileBrowser.Views;

/// <summary>Batch-rename dialog. Returns the accepted proposals, or null if cancelled.</summary>
public partial class BatchRenameWindow : Window
{
    public static readonly IValueConverter NotNullConverter =
        new FuncValueConverter<object?, bool>(v => v is string s ? !string.IsNullOrEmpty(s) : v is not null);

    public static readonly IValueConverter ChangedWeightConverter =
        new FuncValueConverter<bool, FontWeight>(changed => changed ? FontWeight.SemiBold : FontWeight.Normal);

    private BatchRenameViewModel? _vm;

    public BatchRenameWindow() : this([])
    {
    }

    public BatchRenameWindow(IReadOnlyList<FileSystemEntry> entries)
    {
        InitializeComponent();
        DataContext = _vm = new BatchRenameViewModel(entries);
    }

    private void OnApply(object? sender, RoutedEventArgs e) =>
        Close(_vm?.AcceptedProposals);

    private void OnCancel(object? sender, RoutedEventArgs e) =>
        Close(null);
}
