using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// A list that can swallow the next keystroke and report it as a chord — the live capture the
/// hotkey editor needs (PRD §6.6). The list is owner-drawn, so it sees the raw key before the form's
/// dialog-key chain gets a look at it.
/// </summary>
public sealed class KeyCaptureListBox : ListBox
{
    /// <summary>Whether the next non-modifier keystroke is consumed instead of navigating the list.</summary>
    public bool Capturing { get; set; }

    /// <summary>Raised with the captured chord, or <see cref="Keys.Escape"/> when the user cancelled.</summary>
    public event EventHandler<Keys>? ChordCaptured;

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!this.Capturing)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;

        // Wait for a real key: a bare modifier is the start of a chord, not the chord.
        var key = e.KeyData & Keys.KeyCode;
        if (key is Keys.None or Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
            return;

        this.Capturing = false;
        this.ChordCaptured?.Invoke(this, e.KeyData);
    }
}
