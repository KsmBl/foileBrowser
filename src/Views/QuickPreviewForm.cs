using FoileBrowser.Models;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>Spacebar quick-preview window (PRD §6.5). Escape closes it.</summary>
public sealed class QuickPreviewForm : Form
{
    public QuickPreviewForm(PreviewResult preview)
    {
        this.Text = "Quick Preview — " + preview.Title;
        this.Bounds = new(0, 0, 720, 560);
        this.StartPosition = FormStartPosition.CenterParent;

        var inspector = new InspectorView { Dock = DockStyle.Fill };
        inspector.Show(preview);
        this.Controls.Add(inspector);

        var close = new Button { Text = "Close", Bounds = new(-100, -100, 1, 1), Visible = false };
        this.Controls.Add(close);
        this.CancelButton = close;
    }
}
