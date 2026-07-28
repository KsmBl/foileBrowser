using System.Drawing;
using FoileBrowser.Models;
using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Drawing;

namespace FoileBrowser.Views;

/// <summary>
/// The inspector panel (PRD §6.5): the selected entry's name and details over a text, folder-listing
/// or image preview. The same control backs the spacebar quick-preview window.
/// </summary>
public sealed class InspectorView : Panel
{
    private readonly Label _title = new() { Dock = DockStyle.Top, Bounds = new(0, 0, 0, 22), Padding = new(6, 4, 6, 0) };
    private readonly Label _info = new() { Dock = DockStyle.Top, Bounds = new(0, 0, 0, 20), ForeColor = Color.Gray, Padding = new(6, 0, 6, 4) };
    private readonly Label _placeholder = new()
    {
        Dock = DockStyle.Fill,
        Text = "Select an item to preview",
        ForeColor = Color.Gray,
        TextAlign = ContentAlignment.MiddleCenter,
    };

    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
    private readonly TextBox _text = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, AcceptsReturn = true, Visible = false };

    private IImage? _current;

    public InspectorView()
    {
        this.Controls.Add(_placeholder);
        this.Controls.Add(_picture);
        this.Controls.Add(_text);
        this.Controls.Add(_info);
        this.Controls.Add(_title);
    }

    /// <summary>Renders a preview, or the empty-state hint when there is none.</summary>
    public void Show(PreviewResult? preview)
    {
        _current?.Dispose();
        _current = null;

        _title.Text = preview?.Title ?? string.Empty;
        _info.Text = preview?.Info ?? string.Empty;

        if (preview is null)
        {
            _placeholder.Visible = true;
            _picture.Visible = false;
            _text.Visible = false;
            _picture.Image = null;
            return;
        }

        _placeholder.Visible = false;

        if (preview.HasImage && LoadImage(preview.ImagePath!) is { } image)
        {
            _current = image;
            _picture.Image = image;
            _picture.Visible = true;
            _text.Visible = false;
            return;
        }

        _picture.Image = null;
        _picture.Visible = false;
        // An image the toolkit's decoder does not read still gets its details, with a note in place
        // of the pixels rather than an empty panel.
        _text.Text = preview.HasText
            ? preview.Text!
            : preview.HasImage ? "(no preview: unsupported image format)" : string.Empty;
        _text.Visible = true;
    }

    private static IImage? LoadImage(string path)
    {
        try
        {
            return AnimatedImage.Decode(File.ReadAllBytes(path));
        }
        catch (Exception)
        {
            return null; // unreadable or a format the decoder does not cover — the caller shows text
        }
    }
}
