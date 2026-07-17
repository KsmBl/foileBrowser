using System.Diagnostics;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class DiskService : IDiskService
{
    // Filesystems we can create, with the force/quick flags that keep mkfs non-interactive and the
    // per-tool label flag (they disagree: -L for most, -n for FAT, -l for f2fs).
    private static readonly FilesystemType[] Catalogue =
    [
        new("ext4", "ext4", "mkfs.ext4", "-L", ["-F"]),
        new("ext3", "ext3", "mkfs.ext3", "-L", ["-F"]),
        new("ext2", "ext2", "mkfs.ext2", "-L", ["-F"]),
        new("btrfs", "Btrfs", "mkfs.btrfs", "-L", ["-f"]),
        new("xfs", "XFS", "mkfs.xfs", "-L", ["-f"]),
        new("f2fs", "F2FS", "mkfs.f2fs", "-l", ["-f"]),
        new("vfat", "FAT32", "mkfs.vfat", "-n", ["-F", "32"]),
        new("exfat", "exFAT", "mkfs.exfat", "-L", []),
        new("ntfs", "NTFS", "mkfs.ntfs", "-L", ["-Q", "-F"]),
    ];

    // Directories mkfs/wipefs tools commonly live in beyond the normal PATH.
    private static readonly string[] ExtraBinDirs = ["/usr/sbin", "/sbin", "/usr/local/sbin", "/usr/bin", "/bin"];

    public IReadOnlyList<FilesystemType> AvailableFilesystems() =>
        !OperatingSystem.IsLinux() ? [] : Catalogue.Where(f => Which(f.MkfsCommand) is not null).ToList();

    public Task<FormatResult> FormatAsync(string device, string fsId, string? label, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            if (!OperatingSystem.IsLinux())
                return new FormatResult(false, "Formatting is only supported on Linux in this build.");
            if (string.IsNullOrWhiteSpace(device) || !device.StartsWith("/dev/", StringComparison.Ordinal))
                return new FormatResult(false, $"Refusing to format an unrecognised device: “{device}”.");
            if (Catalogue.FirstOrDefault(f => f.Id == fsId) is not { } fs)
                return new FormatResult(false, $"Unknown filesystem type: “{fsId}”.");
            if (IsMountedAtRoot(device))
                return new FormatResult(false, "Refusing to format the device mounted at “/” (the running system).");
            if (Which("pkexec") is null)
                return new FormatResult(false, "pkexec (polkit) is required to format as root but was not found.");
            if (Which("wipefs") is null)
                return new FormatResult(false, "wipefs is required but was not found.");

            // Build the mkfs argv explicitly (no shell interpolation) so a label can't inject anything.
            var mkfs = new List<string> { fs.MkfsCommand };
            mkfs.AddRange(fs.ExtraArgs);
            if (!string.IsNullOrWhiteSpace(label) && fs.LabelFlag is not null)
            {
                mkfs.Add(fs.LabelFlag);
                mkfs.Add(label.Trim());
            }
            mkfs.Add(device);

            // One pkexec prompt does the lot: unmount (ignore if not mounted), wipe old signatures, mkfs.
            const string script = "DEV=\"$1\"; shift; umount \"$DEV\" 2>/dev/null || true; " +
                                   "wipefs -a \"$DEV\" || exit 21; exec \"$@\"";
            var argv = new List<string> { "/bin/sh", "-c", script, "sh", device };
            argv.AddRange(mkfs);

            try
            {
                var psi = new ProcessStartInfo("pkexec")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                foreach (var a in argv)
                    psi.ArgumentList.Add(a);

                var process = Process.Start(psi);
                if (process is null)
                    return new FormatResult(false, "Could not start pkexec.");

                var stderr = process.StandardError.ReadToEnd();
                var stdout = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                    return new FormatResult(true, $"Created {fs.Display} on {device}.");

                // pkexec exits 126/127 when the auth dialog is dismissed or the action is not authorised.
                if (process.ExitCode is 126 or 127)
                    return new FormatResult(false, "Authorization was cancelled or denied.");

                var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                return new FormatResult(false, $"mkfs failed (exit {process.ExitCode}): {detail.Trim()}");
            }
            catch (Exception ex)
            {
                return new FormatResult(false, $"Format failed: {ex.Message}");
            }
        }, cancellationToken);

    /// <summary>True when <paramref name="device"/> currently backs the "/" mount (never format that).</summary>
    private static bool IsMountedAtRoot(string device)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0] == device && parts[1] == "/")
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If we cannot read mounts, err on the safe side and treat as protected.
            return true;
        }
        return false;
    }

    /// <summary>Locates an executable on PATH plus the usual sbin dirs; null if not installed.</summary>
    private static string? Which(string command)
    {
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(ExtraBinDirs);
        foreach (var dir in dirs)
        {
            var candidate = Path.Combine(dir, command);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
