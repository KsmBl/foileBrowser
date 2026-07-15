using FoileBrowser.Models;
using FoileBrowser.Services;

namespace FoileBrowser.Tests;

/// <summary>In-memory filesystem returning a fixed listing for every path.</summary>
internal sealed class FakeFileSystem : IFileSystemService
{
    public List<FileSystemEntry> Entries { get; } = [];
    public List<DriveVolume> Volumes { get; } = [];
    public string? ParentOverride { get; set; } = "/parent";

    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FileSystemEntry>>(Entries.ToList());

    public Task<IReadOnlyList<FileSystemEntry>> ListDrivesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<FileSystemEntry>>([]);

    public Task<IReadOnlyList<DriveVolume>> ListVolumesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DriveVolume>>(Volumes.ToList());

    public string? GetParent(string path) => ParentOverride;

    public bool DirectoryExists(string path) => true;
}

/// <summary>Records trashed paths instead of touching the real OS trash.</summary>
internal sealed class RecordingTrash : ITrashService
{
    public List<string> Trashed { get; } = [];

    public Task TrashAsync(string path, CancellationToken cancellationToken = default)
    {
        Trashed.Add(path);
        return Task.CompletedTask;
    }
}

/// <summary>Returns a fixed preview immediately and records how many times it was asked.</summary>
internal sealed class FakePreview : IPreviewService
{
    public int Calls { get; private set; }
    public FileSystemEntry? Last { get; private set; }

    public Task<PreviewResult> CreateAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
    {
        Calls++;
        Last = entry;
        return Task.FromResult(new PreviewResult
        {
            Kind = PreviewKind.None, Title = entry.Name, Info = "fake",
        });
    }
}

internal static class FakeEntries
{
    public static FileSystemEntry Dir(string name) => new()
    {
        Name = name, FullPath = "/x/" + name, Kind = FileSystemEntryKind.Directory,
    };

    public static FileSystemEntry File(string name, bool hidden = false) => new()
    {
        Name = name, FullPath = "/x/" + name, Kind = FileSystemEntryKind.File, Size = 1, IsHidden = hidden,
    };
}
