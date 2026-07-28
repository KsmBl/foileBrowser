using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>Modal name prompt: returns the entered string, or null when cancelled.</summary>
public sealed class NameInputDialog : Form
{
    private readonly TextBox _name = new() { Bounds = new(16, 42, 348, 26) };

    public NameInputDialog(string initial)
    {
        this.Text = "Rename";
        this.Bounds = new(0, 0, 380, 150);
        this.StartPosition = FormStartPosition.CenterParent;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MinimizeBox = false;
        this.MaximizeBox = false;

        _name.Text = initial;

        var ok = new Button { Text = "OK", Bounds = new(274, 82, 90, 28), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Bounds = new(176, 82, 90, 28) };

        this.Controls.AddRange(
            new Label { Text = "Enter a new name:", Bounds = new(16, 16, 348, 20) },
            _name,
            ok,
            cancel);

        this.AcceptButton = ok;
        this.CancelButton = cancel;
        this.ActiveControl = _name;
        this.Load += (_, _) => _name.SelectAll();
    }

    /// <summary>The trimmed name, or null when the user cancelled or cleared the box.</summary>
    public static Task<string?> RequestAsync(Form owner, string initial)
    {
        var dialog = new NameInputDialog(initial);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
            return Task.FromResult<string?>(null);

        var name = dialog._name.Text.Trim();
        return Task.FromResult(name.Length == 0 ? null : name);
    }
}
