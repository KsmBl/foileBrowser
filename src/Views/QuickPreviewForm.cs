using FoileBrowser.Models;
using FoileBrowser.Services;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>Spacebar quick-preview window (PRD §6.5). Escape closes it.</summary>
public sealed class QuickPreviewForm : Form
{
    public QuickPreviewForm(PreviewResult preview, ThumbnailService thumbnails)
    {
        this.Text = "Quick Preview — " + preview.Title;
        this.Bounds = new(0, 0, 720, 560);
        this.StartPosition = FormStartPosition.CenterParent;

        Ui.Outline(this);

        var pane = new PreviewPane(thumbnails) { Dock = DockStyle.Fill };
        pane.Show(preview);
        this.Controls.Add(pane);
        this.FormClosed += (_, _) => pane.Detach();

        var close = new Button { Text = "Close", Bounds = new(-100, -100, 1, 1), Visible = false };
        this.Controls.Add(close);
        this.CancelButton = close;
    }
}
