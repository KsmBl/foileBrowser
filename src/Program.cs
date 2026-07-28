using Hawkynt.NativeForms;
using Hawkynt.NativeForms.Backends;
using Hawkynt.NativeForms.Backends.Gtk;
using Hawkynt.NativeForms.Backends.Windows;

namespace FoileBrowser;

internal sealed class Program
{
    /// <summary>
    /// Registers the backends this build ships and hands the shell window to the message loop.
    /// Registration is explicit construction, so the trimmer sees exactly which backends are
    /// reachable and only the one whose <c>IsSupported</c> matches the running OS is ever realized.
    /// There is no renderer to choose: every control is either a platform widget or painted straight
    /// onto the window, so no GPU stack is mapped into the process at all (PRD §6.12).
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        BackendRegistry.Register(new Win32Backend());
        BackendRegistry.Register(new GtkBackend());

        Application.Run(App.CreateShell());
    }
}
