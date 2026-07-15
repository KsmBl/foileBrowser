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

                results.Add(new DriveVolume
                {
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? root : drive.VolumeLabel,
                    RootPath = root,
                    FreeBytes = free,
                    TotalBytes = total,
                    FileSystem = fs,
                    Kind = ClassifyVolume(root, drive.DriveType),
                });
            }

            if (!OperatingSystem.IsWindows())
                results.AddRange(EnumerateGvfsMounts(cancellationToken));

            return results;
        }, cancellationToken);

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
