using System.Collections.Concurrent;
using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <summary>
/// Decides how the copy engine should move bytes for a given source/destination pair (PRD §6.3).
/// The heuristic: interleaving reads and writes on a <em>single</em> mechanical or optical spindle
/// makes the head seek back and forth and destroys throughput, so those get a large sequential
/// slurp; everything else (SSDs, cross-device transfers) gets overlapped read+write.
/// </summary>
internal static class DriveProfiler
{
    private readonly record struct Profile(string Device, bool Sequential);

    // Profiles are stable for the life of the process; cache them to keep Auto cheap per file.
    private static readonly ConcurrentDictionary<string, Profile> Cache = new();

    public static CopyStrategy Recommend(string source, string dest, CopyOptions options)
    {
        if (options.Strategy != CopyStrategy.Auto)
            return options.Strategy;

        try
        {
            var s = ProfileFor(source);
            var d = ProfileFor(dest);

            // Same physical device and at least one side is slow-seek media → don't interleave.
            if (s.Device.Length > 0 && s.Device == d.Device && (s.Sequential || d.Sequential))
                return CopyStrategy.Sequential;
        }
        catch
        {
            // Profiling is best-effort; fall back to the safe, generally-fast overlapped path.
        }

        return CopyStrategy.Overlapped;
    }

    private static Profile ProfileFor(string path)
    {
        // Only Linux exposes a cheap, dependency-free way to classify the backing device; on other
        // platforms we assume fast random access (overlapped), which is correct for modern SSDs.
        if (!OperatingSystem.IsLinux())
            return default;

        var mount = FindMount(path);
        return Cache.GetOrAdd(mount.Device is { Length: > 0 } dev ? dev : path, _ =>
        {
            var optical = mount.FsType is "iso9660" or "udf";
            var rotational = mount.Device.Length > 0 && IsRotational(mount.Device);
            return new Profile(mount.Device, optical || rotational);
        });
    }

    private readonly record struct Mount(string Device, string FsType);

    /// <summary>Finds the /proc/mounts entry whose mount point is the longest prefix of the path.</summary>
    private static Mount FindMount(string path)
    {
        var full = Path.GetFullPath(path);
        var best = default(Mount);
        var bestLen = -1;

        foreach (var line in File.ReadLines("/proc/mounts"))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;

            var mountPoint = Unescape(parts[1]);
            if (mountPoint.Length <= bestLen || !IsUnder(full, mountPoint))
                continue;

            bestLen = mountPoint.Length;
            best = new Mount(Unescape(parts[0]), parts[2]);
        }

        return best;
    }

    private static bool IsUnder(string path, string mountPoint)
    {
        if (mountPoint == "/")
            return true;
        return path == mountPoint
            || path.StartsWith(mountPoint.TrimEnd('/') + "/", StringComparison.Ordinal);
    }

    /// <summary>Maps /dev/sda3 → sda and reads /sys/block/sda/queue/rotational (1 = spinning disk).</summary>
    private static bool IsRotational(string device)
    {
        if (!device.StartsWith("/dev/", StringComparison.Ordinal))
            return false;

        var name = device["/dev/".Length..];
        var disk = BaseDisk(name);
        var flag = $"/sys/block/{disk}/queue/rotational";
        return File.Exists(flag) && File.ReadAllText(flag).Trim() == "1";
    }

    // Strip a trailing partition number: sda3 → sda, nvme0n1p2 → nvme0n1, mmcblk0p1 → mmcblk0.
    private static string BaseDisk(string name)
    {
        if ((name.StartsWith("nvme", StringComparison.Ordinal) || name.StartsWith("mmcblk", StringComparison.Ordinal))
            && name.LastIndexOf('p') is var p and > 0)
            return name[..p];

        return name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
    }

    // /proc/mounts octal-escapes spaces and a few other characters (e.g. "\040" for space).
    private static string Unescape(string value) =>
        !value.Contains('\\') ? value
            : System.Text.RegularExpressions.Regex.Replace(
                value, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());
}
