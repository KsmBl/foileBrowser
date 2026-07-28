using FoileBrowser.Services;
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
        var requested = FolderArgument(args);

        // One process serves every window. A second launch hands its folder to the copy already
        // running and exits, so it costs a socket round-trip instead of a whole second runtime
        // (PRD §6.12). --standalone opts out, which is what the screenshot runs use.
        var single = Array.IndexOf(args, "--standalone") < 0;
        InstanceServer? server = null;
        if (single)
        {
            server = InstanceServer.Claim(requested, out var handedOver);
            if (handedOver)
                return;
        }

        BackendRegistry.Register(new Win32Backend());
        BackendRegistry.Register(new GtkBackend());

        var shell = App.CreateShell();
        if (requested is not null)
            shell.OpenAtStartup(requested);

        if (server is not null)
        {
            // The accept loop runs off the UI thread, so the request is marshalled back onto it.
            server.OpenRequested += (_, path) => shell.BeginInvoke(() => shell.OpenPath(path));
            server.Start();
        }

        ArmScreenshot(shell, args);
        try
        {
            Application.Run(shell);
        }
        finally
        {
            server?.Dispose();
        }
    }

    /// <summary>The folder to open, taken from the first argument that is not a switch.</summary>
    private static string? FolderArgument(string[] args)
    {
        for (var i = 0; i < args.Length; ++i)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (args[i] is "--screenshot" or "--screenshot-delay")
                    ++i; // skip that switch's value
                continue;
            }

            return Directory.Exists(args[i]) ? Path.GetFullPath(args[i]) : null;
        }

        return null;
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
            Console.WriteLine(Screenshot.TryCapture(path, out var windows)
                ? $"wrote {path} ({windows} window(s))"
                : $"could not capture to {path}");
            shell.Close();
        };
        shell.Load += (_, _) => timer.Start();
    }
}
