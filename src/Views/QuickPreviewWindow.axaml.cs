using Avalonia.Controls;
using Avalonia.Input;
using FoileBrowser.Models;

namespace FoileBrowser.Views;

/// <summary>Spacebar quick-preview popup (PRD §6.5). Closes on Space or Escape.</summary>
public partial class QuickPreviewWindow : Window
{
    public QuickPreviewWindow() : this(null)
    {
    }

    public QuickPreviewWindow(PreviewResult? preview)
    {
        InitializeComponent();
        DataContext = preview;
        if (preview is not null)
            Title = "Quick Preview — " + preview.Title;

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Space)
                Close();
        };
    }
}
