using FoileBrowser.Views;
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

        var shell = App.CreateShell();
        ArmScreenshot(shell, args);
        Application.Run(shell);
    }

    /// <summary>
    /// <c>--screenshot &lt;path&gt; [--screenshot-delay &lt;ms&gt;]</c> photographs the window once it has
    /// settled and quits. This is how the images in the README are regenerated, and how a change can
    /// be checked on a machine with nobody watching the screen — see <see cref="Screenshot"/>.
    /// </summary>
    private static void ArmScreenshot(Form shell, string[] args)
    {
        var index = Array.IndexOf(args, "--screenshot");
        if (index < 0 || index + 1 >= args.Length)
            return;

        var path = args[index + 1];
        var delayIndex = Array.IndexOf(args, "--screenshot-delay");
        var delay = delayIndex >= 0 && delayIndex + 1 < args.Length && int.TryParse(args[delayIndex + 1], out var ms)
            ? Math.Clamp(ms, 100, 60_000)
            : 2500;

        // A one-shot timer on the UI thread: the directory listing arrives asynchronously, so the
        // shot has to wait for the first frame that actually has content in it.
        var timer = new Hawkynt.NativeForms.Timer { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Console.WriteLine(Screenshot.TryCapture(path) ? $"wrote {path}" : $"could not capture to {path}");
            shell.Close();
        };
        shell.Load += (_, _) => timer.Start();
    }
}
