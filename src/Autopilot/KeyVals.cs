using Hawkynt.NativeForms;

namespace FoileBrowser.Autopilot;

/// <summary>The GDK key symbols (<c>gdkkeysyms.h</c>) behind the keys the walkthrough presses.</summary>
internal sealed partial class Driver
{
    /// <summary>GDK modifier masks: Shift is bit 0, Control bit 2, Alt (Mod1) bit 3.</summary>
    internal const uint ShiftMask = 1 << 0;

    internal const uint ControlMask = 1 << 2;

    internal const uint AltMask = 1 << 3;

    private static uint KeyVal(Keys key) => key switch
    {
        Keys.Back => 0xff08,
        Keys.Tab => 0xff09,
        Keys.Enter => 0xff0d,
        Keys.Escape => 0xff1b,
        Keys.Space => 0x020,
        Keys.Delete => 0xffff,
        Keys.Home => 0xff50,
        Keys.Left => 0xff51,
        Keys.Up => 0xff52,
        Keys.Right => 0xff53,
        Keys.Down => 0xff54,
        Keys.PageUp => 0xff55,
        Keys.PageDown => 0xff56,
        Keys.End => 0xff57,
        Keys.F2 => 0xffbf,
        Keys.F5 => 0xffc2,
        >= Keys.A and <= Keys.Z => (uint)('a' + (key - Keys.A)),
        _ => throw new NotSupportedException($"no key symbol for {key}"),
    };
}
