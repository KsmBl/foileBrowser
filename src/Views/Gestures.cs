using FoileBrowser.ViewModels;
using Hawkynt.NativeForms;

namespace FoileBrowser.Views;

/// <summary>
/// Translates between the persisted hotkey strings ("Ctrl+Shift+N") and the toolkit's
/// <see cref="Keys"/> chords (PRD §6.6). Both directions are needed: the menu bar dispatches from a
/// parsed chord, and the keybind editor captures a live keystroke and writes the string back.
/// Deliberately table-driven and reflection-free so it survives trimming.
/// </summary>
public static class Gestures
{
    /// <summary>
    /// Runs a mouse press through the browser's back/forward buttons, reporting whether it was one.
    /// </summary>
    /// <remarks>
    /// The two buttons under the thumb mean the same here as they do in a browser, which is the only
    /// thing anyone expects of them. Both listings ask, because either can be under the pointer.
    /// </remarks>
    public static bool TryNavigate(MouseEventArgs e, FileTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(tab);

        var command = e.Button switch
        {
            MouseButtons.XButton1 => tab.GoBackCommand,
            MouseButtons.XButton2 => tab.GoForwardCommand,
            _ => null,
        };

        if (command is null)
            return false;

        // Pressing back at the start of the history is not an error, and must not fall through to
        // the selection handling either — the press was still meant for navigation.
        if (command.CanExecute(null))
            command.Execute(null);

        return true;
    }

    /// <summary>Parses a gesture string; returns <see cref="Keys.None"/> for null, empty or unparseable input.</summary>
    public static Keys Parse(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            return Keys.None;

        var chord = Keys.None;
        var key = Keys.None;

        foreach (var raw in gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": chord |= Keys.Control; break;
                case "SHIFT": chord |= Keys.Shift; break;
                case "ALT": chord |= Keys.Alt; break;
                default:
                    if (ParseKey(raw) is not { } parsed)
                        return Keys.None; // an unknown token makes the whole gesture unusable
                    key = parsed;
                    break;
            }
        }

        return key == Keys.None ? Keys.None : chord | key;
    }

    /// <summary>Formats a chord back into the persisted string form, or null when it carries no key.</summary>
    public static string? Format(Keys chord)
    {
        var key = chord & Keys.KeyCode;
        if (key is Keys.None or Keys.ShiftKey or Keys.ControlKey or Keys.Menu || KeyName(key) is not { } name)
            return null;

        var text = string.Empty;
        if (chord.HasFlag(Keys.Control))
            text += "Ctrl+";
        if (chord.HasFlag(Keys.Shift))
            text += "Shift+";
        if (chord.HasFlag(Keys.Alt))
            text += "Alt+";
        return text + name;
    }

    private static Keys? ParseKey(string token) => token.ToUpperInvariant() switch
    {
        "DELETE" or "DEL" => Keys.Delete,
        "INSERT" or "INS" => Keys.Insert,
        "ENTER" or "RETURN" => Keys.Enter,
        "ESCAPE" or "ESC" => Keys.Escape,
        "SPACE" => Keys.Space,
        "TAB" => Keys.Tab,
        "BACK" or "BACKSPACE" => Keys.Back,
        "LEFT" => Keys.Left,
        "RIGHT" => Keys.Right,
        "UP" => Keys.Up,
        "DOWN" => Keys.Down,
        "HOME" => Keys.Home,
        "END" => Keys.End,
        "PAGEUP" or "PGUP" => Keys.PageUp,
        "PAGEDOWN" or "PGDN" => Keys.PageDown,
        ['F', var d] when d is >= '1' and <= '9' => Keys.F1 + (d - '1'),
        "F10" => Keys.F10,
        "F11" => Keys.F11,
        "F12" => Keys.F12,
        [var c] when c is >= 'A' and <= 'Z' => Keys.A + (c - 'A'),
        [var c] when c is >= '0' and <= '9' => Keys.D0 + (c - '0'),
        _ => null,
    };

    private static string? KeyName(Keys key) => key switch
    {
        Keys.Delete => "Delete",
        Keys.Insert => "Insert",
        Keys.Enter => "Enter",
        Keys.Escape => "Escape",
        Keys.Space => "Space",
        Keys.Tab => "Tab",
        Keys.Back => "Back",
        Keys.Left => "Left",
        Keys.Right => "Right",
        Keys.Up => "Up",
        Keys.Down => "Down",
        Keys.Home => "Home",
        Keys.End => "End",
        Keys.PageUp => "PageUp",
        Keys.PageDown => "PageDown",
        >= Keys.F1 and <= Keys.F12 => "F" + (key - Keys.F1 + 1),
        >= Keys.A and <= Keys.Z => ((char)('A' + (key - Keys.A))).ToString(),
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        _ => null,
    };
}
