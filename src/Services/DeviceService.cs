using System.Diagnostics;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class DeviceService : IDeviceService
{
    public Task EjectAsync(string mountPath, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        if (OperatingSystem.IsWindows())
        {
            // PowerShell dismount by drive letter (best effort).
            var letter = Path.GetPathRoot(mountPath)?.TrimEnd('\\', '/');
            if (!string.IsNullOrEmpty(letter))
                TryRun("powershell", ["-NoProfile", "-Command",
                    $"(New-Object -comObject Shell.Application).Namespace(17).ParseName('{letter}').InvokeVerb('Eject')"]);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            TryRun("diskutil", ["eject", mountPath]);
            return;
        }

        // Linux: gio handles both udisks-mounted media and GVfs mounts; udisksctl as a fallback.
        if (!TryRun("gio", ["mount", "-e", mountPath]) && !TryRun("gio", ["mount", "-u", mountPath]))
            TryRun("udisksctl", ["unmount", "-b", mountPath]);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<string?> MountAsync(string device, CancellationToken cancellationToken = default)
        => Task.Run<string?>(() =>
        {
            // Windows and macOS mount removable media themselves; there is nothing here to ask for.
            if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(device))
                return null;

            // udisksctl is the one that works as an ordinary user, via polkit — which is the whole
            // point: mounting a stick should not need a password, let alone a terminal.
            if (!TryRun("udisksctl", ["mount", "-b", device]))
                TryRun("gio", ["mount", "-d", device]);

            // Where it landed is read back from the kernel rather than scraped out of the tool's
            // message, which differs between udisks versions and is translated.
            return MountPointOf(device);
        }, cancellationToken);

    /// <summary>The mount point of a device according to /proc/mounts, or null if it is not mounted.</summary>
    private static string? MountPointOf(string device)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && string.Equals(parts[0], device, StringComparison.Ordinal))
                    return Unescape(parts[1]);
            }
        }
        catch (IOException)
        {
            // No /proc/mounts to read; the caller treats that as "did not mount".
        }

        return null;
    }

    /// <summary>/proc/mounts octal-escapes spaces and a few other characters ("\040" for space).</summary>
    private static string Unescape(string value) =>
        !value.Contains('\\') ? value
            : System.Text.RegularExpressions.Regex.Replace(
                value, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());

    private static bool TryRun(string fileName, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardError = true };
            foreach (var a in args)
                psi.ArgumentList.Add(a);
            var process = Process.Start(psi);
            if (process is null)
                return false;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
