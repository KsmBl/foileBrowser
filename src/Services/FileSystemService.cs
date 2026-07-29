using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class FileSystemService : IFileSystemService
{
    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(
        string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var results = new List<FileSystemEntry>();
            var directory = new DirectoryInfo(path);

            // Enumerating lazily lets us honour cancellation mid-scan on huge directories
            // (PRD §6.4 search cancellation, §6.12 100k-entry lists).
            foreach (var info in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(ToEntry(info));
            }

            return results;
        }, cancellationToken);

    public Task<IReadOnlyList<FileSystemEntry>> ListDrivesAsync(
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var results = new List<FileSystemEntry>();

            foreach (var drive in DriveInfo.GetDrives())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!drive.IsReady)
                    continue;

                var root = drive.RootDirectory;
                var label = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? root.FullName
                    : $"{drive.VolumeLabel} ({root.FullName})";

                results.Add(new FileSystemEntry
                {
                    Name = label,
                    FullPath = root.FullName,
                    Kind = FileSystemEntryKind.Drive,
                    Modified = SafeLastWriteTime(root),
                });
            }

            return results;
        }, cancellationToken);

    // Real storage filesystems worth showing as drives; everything else (proc, sysfs, cgroup,
    // tmpfs, …) is a pseudo-filesystem we hide from the drive list (PRD §6.10).
    private static readonly HashSet<string> RealFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "ext2", "ext3", "ext4", "btrfs", "xfs", "f2fs", "zfs", "reiserfs", "jfs", "nilfs2",
        "ntfs", "ntfs3", "fuseblk", "vfat", "exfat", "msdos", "iso9660", "udf", "hfs", "hfsplus",
        "apfs", "NTFS", "FAT32", "exFAT", "APFS", "HFS",
    };

    public Task<IReadOnlyList<DriveVolume>> ListVolumesAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<DriveVolume>>(() =>
        {
            var results = new List<DriveVolume>();
            var mounts = OperatingSystem.IsLinux() ? ReadMountDevices() : null;

            foreach (var drive in DriveInfo.GetDrives())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!drive.IsReady)
                    continue;

                var root = drive.RootDirectory.FullName;
                string? fs = null;
                long? free = null, total = null;
                try
                {
                    fs = drive.DriveFormat;
                    free = drive.AvailableFreeSpace;
                    total = drive.TotalSize;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Capacity unavailable (e.g. permission); still consider the volume.
                }

                // On Unix, DriveInfo lists every mount; keep only real storage plus the root.
                if (!OperatingSystem.IsWindows() && root != "/"
                    && (fs is null || !RealFileSystems.Contains(fs)))
                    continue;

                // Resolve the backing device + physical disk so partitions group under their drive.
                var device = mounts is not null && mounts.TryGetValue(root, out var dev) ? dev : null;
                var disk = DiskOf(device);

                results.Add(new DriveVolume
                {
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : drive.VolumeLabel,
                    RootPath = root,
                    FreeBytes = free,
                    TotalBytes = total,
                    FileSystem = fs,
                    Device = device,
                    Disk = disk,
                    // Prefer the real removable flag from /sys; fall back to the DriveType heuristic.
                    Kind = disk is not null ? ClassifyDisk(disk) : ClassifyVolume(root, drive.DriveType),
                });
            }

            if (!OperatingSystem.IsWindows())
                results.AddRange(EnumerateGvfsMounts(cancellationToken));

            if (OperatingSystem.IsLinux())
                results.AddRange(EnumerateUnmountedPartitions(mounts, cancellationToken));

            return results;
        }, cancellationToken);

    /// <summary>
    /// Removable partitions that are present but not mounted (PRD §6.10). Without these a stick that
    /// the desktop did not auto-mount is invisible, and mounting it means a terminal — which is the
    /// whole thing this is here to avoid.
    /// </summary>
    private static IEnumerable<DriveVolume> EnumerateUnmountedPartitions(
        Dictionary<string, string>? mounts, CancellationToken cancellationToken)
    {
        var results = new List<DriveVolume>();
        var mounted = mounts is null
            ? []
            : new HashSet<string>(mounts.Values, StringComparer.Ordinal);

        var labels = ReadDeviceLabels();

        foreach (var diskDir in SafeDirectories("/sys/block"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var disk = Path.GetFileName(diskDir);

            // Only removable spindles: an unmounted partition of the system disk is somebody's
            // recovery volume or a foreign filesystem, not something to offer as a click target.
            if (!ReadFlag($"{diskDir}/removable"))
                continue;

            foreach (var partitionDir in SafeDirectories(diskDir))
            {
                var name = Path.GetFileName(partitionDir);
                if (!name.StartsWith(disk, StringComparison.Ordinal) || !File.Exists($"{partitionDir}/partition"))
                    continue;

                var device = "/dev/" + name;
                if (mounted.Contains(device))
                    continue;

                results.Add(new DriveVolume
                {
                    Label = labels.TryGetValue(device, out var label) ? label : name,
                    RootPath = string.Empty,
                    TotalBytes = ReadSectors($"{partitionDir}/size"),
                    Device = device,
                    Disk = disk,
                    Kind = VolumeKind.Removable,
                    IsMounted = false,
                });
            }
        }

        return results;
    }

    /// <summary>Filesystem labels by device, from the by-label symlinks udev maintains.</summary>
    private static Dictionary<string, string> ReadDeviceLabels()
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var link in Directory.EnumerateFiles("/dev/disk/by-label"))
            {
                var target = File.ResolveLinkTarget(link, returnFinalTarget: true);
                if (target is not null)
                    labels[target.FullName] = Path.GetFileName(link).Replace("\\x20", " ");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No by-label directory (no udev, or nothing labelled) — device names will do.
        }

        return labels;
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool ReadFlag(string path)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path).Trim() == "1";
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>A /sys size, which counts 512-byte sectors whatever the device's real sector size.</summary>
    private static long? ReadSectors(string path)
    {
        try
        {
            return File.Exists(path) && long.TryParse(File.ReadAllText(path).Trim(), out var sectors)
                ? sectors * 512
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Maps each mount point to its backing device via /proc/mounts (Linux).</summary>
    private static Dictionary<string, string> ReadMountDevices()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in File.ReadLines("/proc/mounts"))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && parts[0].StartsWith("/dev/", StringComparison.Ordinal))
                    map[UnescapeMount(parts[1])] = parts[0];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No /proc/mounts (unusual) — grouping just won't have device info.
        }
        return map;
    }

    /// <summary>Strips a partition suffix: /dev/sda3 → sda, /dev/nvme0n1p2 → nvme0n1, mmcblk0p1 → mmcblk0.</summary>
    private static string? DiskOf(string? device)
    {
        if (device is null || !device.StartsWith("/dev/", StringComparison.Ordinal))
            return null;
        var name = device["/dev/".Length..];

        if ((name.StartsWith("nvme", StringComparison.Ordinal) || name.StartsWith("mmcblk", StringComparison.Ordinal))
            && name.LastIndexOf('p') is var p and > 0)
            return name[..p];

        var stripped = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        return stripped.Length == 0 ? name : stripped;
    }

    /// <summary>A disk is Removable when /sys/block/&lt;disk&gt;/removable is 1 (USB sticks, SD cards).</summary>
    private static VolumeKind ClassifyDisk(string disk)
    {
        try
        {
            var flag = $"/sys/block/{disk}/removable";
            if (File.Exists(flag) && File.ReadAllText(flag).Trim() == "1")
                return VolumeKind.Removable;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unknown — treat as fixed.
        }
        return VolumeKind.Fixed;
    }

    private static string UnescapeMount(string value) =>
        !value.Contains('\\') ? value
            : System.Text.RegularExpressions.Regex.Replace(
                value, @"\\([0-7]{3})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 8)).ToString());

    private static VolumeKind ClassifyVolume(string root, DriveType type)
    {
        if (type == DriveType.Removable)
            return VolumeKind.Removable;
        // Media auto-mount locations are treated as removable for sidebar grouping.
        if (root.StartsWith("/media/", StringComparison.Ordinal)
            || root.StartsWith("/run/media/", StringComparison.Ordinal)
            || root.StartsWith("/mnt/", StringComparison.Ordinal))
            return VolumeKind.Removable;
        return VolumeKind.Fixed;
    }

    /// <summary>Lists GVfs/GIO mounts (MTP phones, cameras, SMB, …) under /run/user/&lt;uid&gt;/gvfs (PRD §6.10).</summary>
    private static IEnumerable<DriveVolume> EnumerateGvfsMounts(CancellationToken cancellationToken)
    {
        var uid = GetUnixUid();
        var gvfs = uid is null ? null : $"/run/user/{uid}/gvfs";
        if (gvfs is null || !Directory.Exists(gvfs))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(gvfs))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new DriveVolume
            {
                Label = PrettifyGvfsName(Path.GetFileName(dir)),
                RootPath = dir,
                Kind = VolumeKind.Gvfs,
            };
        }
    }

    private static string PrettifyGvfsName(string raw)
    {
        // e.g. "mtp:host=SAMSUNG_..." → "MTP", "smb-share:server=nas,share=x" → "SMB-SHARE"
        var scheme = raw.Split(':', 2)[0];
        return scheme.ToUpperInvariant();
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getuid")]
    private static extern uint LibcGetUid();

    private static uint? GetUnixUid()
    {
        // XDG_RUNTIME_DIR is /run/user/<uid> on desktop sessions; fall back to getuid(2).
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runtime) && uint.TryParse(Path.GetFileName(runtime.TrimEnd('/')), out var uid))
            return uid;

        try { return LibcGetUid(); }
        catch { return null; }
    }

    public string? GetParent(string path)
    {
        try
        {
            // Trim trailing separators so Path.GetDirectoryName("/foo/") -> "/foo"'s parent.
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
                return null;

            var parent = Directory.GetParent(trimmed);
            return parent?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    private static FileSystemEntry ToEntry(FileSystemInfo info)
    {
        var isDirectory = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
        var isHidden = (info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden
            || (info.Attributes & FileAttributes.System) == FileAttributes.System
            // Unix convention: dot-prefixed entries are hidden.
            || info.Name.StartsWith('.');

        return new FileSystemEntry
        {
            Name = info.Name,
            FullPath = info.FullName,
            Kind = isDirectory ? FileSystemEntryKind.Directory : FileSystemEntryKind.File,
            Size = isDirectory ? null : SafeLength(info),
            Modified = SafeLastWriteTime(info),
            IsHidden = isHidden,
        };
    }

    private static long? SafeLength(FileSystemInfo info)
    {
        try
        {
            return info is FileInfo file ? file.Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTimeOffset? SafeLastWriteTime(FileSystemInfo info)
    {
        try
        {
            return info.LastWriteTime;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
