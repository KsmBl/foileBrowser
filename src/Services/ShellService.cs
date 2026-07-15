using System.Diagnostics;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class ShellService : IShellService
{
    // Linux terminals to try, in order, until one launches.
    private static readonly string[] LinuxTerminals =
        ["x-terminal-emulator", "kitty", "alacritty", "gnome-terminal", "konsole", "xfce4-terminal", "xterm"];

    public Task OpenAsync(string path) => Task.Run(() =>
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", [path]);
        else
            Process.Start("xdg-open", [path]);
    });

    public Task OpenTerminalAsync(string directory) => Task.Run(() =>
    {
        if (OperatingSystem.IsWindows())
        {
            // Prefer Windows Terminal, fall back to cmd.
            if (!TryStart("wt.exe", ["-d", directory]))
                Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = directory, UseShellExecute = true });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", ["-a", "Terminal", directory]);
            return;
        }

        foreach (var term in LinuxTerminals)
        {
            if (TryStart(term, [], directory))
                return;
        }
        throw new InvalidOperationException("No supported terminal emulator was found.");
    });

    private static bool TryStart(string fileName, string[] args, string? workingDir = null)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName) { UseShellExecute = false };
            if (workingDir is not null)
                psi.WorkingDirectory = workingDir;
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            return Process.Start(psi) is not null;
        }
        catch (Exception)
        {
            return false; // not installed / failed to launch
        }
    }
}
