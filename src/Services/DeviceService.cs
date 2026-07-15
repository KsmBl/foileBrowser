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
