using System.Diagnostics;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class ShellService : IShellService
{
    // Terminals to try, in order, until one launches (used when no terminal is configured).
    private static readonly string[] LinuxTerminals =
        ["x-terminal-emulator", "kitty", "alacritty", "wezterm", "foot", "ghostty", "gnome-terminal",
         "konsole", "xfce4-terminal", "mate-terminal", "lxterminal", "terminator", "tilix",
         "urxvt", "st", "xterm"];

    private static readonly string[] WindowsTerminals = ["wt.exe", "pwsh.exe", "powershell.exe", "cmd.exe"];

    private static readonly string[] MacTerminals = ["Terminal", "iTerm"];

    /// <inheritdoc />
    public string? TerminalCommand { get; set; }

    /// <summary>
    /// The terminals of this platform that are actually installed, for the settings picker (PRD §6.9).
    /// </summary>
    public static IReadOnlyList<string> DetectTerminals()
    {
        if (OperatingSystem.IsMacOS())
            return MacTerminals;
        var candidates = OperatingSystem.IsWindows() ? WindowsTerminals : LinuxTerminals;
        return [.. candidates.Where(OnPath)];
    }

    private static bool OnPath(string exe)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;
        var exts = OperatingSystem.IsWindows() ? new[] { "", ".exe", ".cmd" } : [""];
        return path.Split(Path.PathSeparator).Any(dir =>
            !string.IsNullOrEmpty(dir) && exts.Any(ext => File.Exists(Path.Combine(dir, exe + ext))));
    }

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
        // A configured terminal wins; fall back to auto-detection when it isn't set or fails to start.
        if (TerminalCommand is { Length: > 0 } configured && TryStartConfigured(configured, directory))
            return;

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

    /// <summary>
    /// Runs the user's configured terminal command. <c>{dir}</c> anywhere in the command line is
    /// substituted with the folder; if it doesn't appear, the folder is used as the working directory.
    /// </summary>
    private static bool TryStartConfigured(string command, string directory)
    {
        var parts = SplitCommand(command);
        if (parts.Count == 0)
            return false;

        var usesDir = command.Contains("{dir}", StringComparison.Ordinal);
        var exe = parts[0].Replace("{dir}", directory, StringComparison.Ordinal);
        var args = parts.Skip(1).Select(a => a.Replace("{dir}", directory, StringComparison.Ordinal)).ToArray();

        if (OperatingSystem.IsMacOS() && !command.Contains(' ') && !usesDir)
            return TryStart("open", ["-a", exe, directory]);

        return TryStart(exe, args, usesDir ? null : directory);
    }

    /// <summary>Splits a command line on spaces, honouring double quotes around arguments.</summary>
    private static List<string> SplitCommand(string command)
    {
        List<string> parts = [];
        var current = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var c in command)
        {
            if (c == '"')
                quoted = !quoted;
            else if (c == ' ' && !quoted)
            {
                if (current.Length > 0)
                    parts.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }
        if (current.Length > 0)
            parts.Add(current.ToString());
        return parts;
    }

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
