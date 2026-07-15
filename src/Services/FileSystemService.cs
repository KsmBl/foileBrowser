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
