using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using FoileBrowser.Models;
using Microsoft.Win32.SafeHandles;

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
        if (OperatingSystem.IsLinux())
            return LinuxProfileFor(path);
        if (OperatingSystem.IsWindows())
            return WindowsProfileFor(path);

        // Other platforms: assume fast random access (overlapped), correct for modern SSDs.
        return default;
    }

    // ---- Linux: /proc/mounts + /sys/block ----

    private static Profile LinuxProfileFor(string path)
    {
        var mount = FindMount(path);
        return Cache.GetOrAdd(mount.Device is { Length: > 0 } dev ? dev : path, _ =>
        {
            var optical = mount.FsType is "iso9660" or "udf";
            var rotational = mount.Device.Length > 0 && IsRotational(mount.Device);
            return new Profile(mount.Device, optical || rotational);
        });
    }

    // ---- Windows: DriveType + IncursSeekPenalty query, cached per volume root ----

    [SupportedOSPlatform("windows")]
    private static Profile WindowsProfileFor(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(root))
            return default;

        return Cache.GetOrAdd(root, r =>
        {
            var sequential = false;
            try
            {
                var type = new DriveInfo(r).DriveType;
                sequential = type == DriveType.CDRom || (type == DriveType.Fixed && HasSeekPenalty(r));
            }
            catch
            {
                // Unreadable/odd volume — leave as overlapped.
            }
            return new Profile(r, sequential);
        });
    }

    [SupportedOSPlatform("windows")]
    private static bool HasSeekPenalty(string root)
    {
        var volume = root.TrimEnd('\\', '/'); // "C:\" -> "C:"
        if (volume.Length < 2)
            return false;

        using var handle = CreateFileW(
            $@"\\.\{volume}", 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            return false;

        var query = new StoragePropertyQuery
        {
            PropertyId = StorageDeviceSeekPenaltyProperty,
            QueryType = PropertyStandardQuery,
        };

        return DeviceIoControl(
                   handle, IoctlStorageQueryProperty,
                   ref query, Marshal.SizeOf<StoragePropertyQuery>(),
                   out var descriptor, Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(),
                   out _, IntPtr.Zero)
               && descriptor.IncursSeekPenalty;
    }

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;
    private const uint OpenExisting = 3;
    private const uint FileShareRead = 1;
    private const uint FileShareWrite = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceSeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;
        [MarshalAs(UnmanagedType.U1)] public bool IncursSeekPenalty;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        ref StoragePropertyQuery lpInBuffer, int nInBufferSize,
        out DeviceSeekPenaltyDescriptor lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);

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
