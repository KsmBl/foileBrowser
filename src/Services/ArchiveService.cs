using Compression.Registry;
using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class ArchiveService : IArchiveService
{
    // CompressionWorkbench ships one descriptor per format in separate assemblies with no central
    // bootstrap. A source generator (FoileBrowser.Generators) enumerates them at compile time and
    // emits static `new XxxDescriptor()` registrations — no runtime reflection, so archives are
    // trim/NativeAOT-safe. Registration runs once, lazily, on first archive use.
    private static readonly Lazy<bool> Registration = new(RegisterAllFormats);

    private static bool RegisterAllFormats()
    {
        FormatRegistry.Initialize();
        FoileBrowser.Generated.GeneratedFormats.RegisterAll();
        return true;
    }

    /// <summary>Runs the one-time registration, for a caller that wants to read the registry itself.</summary>
    internal static void EnsureFormatsRegistered() => _ = Registration.Value;

    private static IFormatDescriptor? DescriptorFor(string path)
    {
        _ = Registration.Value;
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
            return null;

        // Scan the registry directly rather than trusting GetByExtension, whose extension index can
        // return a non-matching descriptor once every format is registered (e.g. resolving ".zip" to
        // the VDI reader). Prefer a descriptor that both claims the extension and can list entries.
        var matches = FormatRegistry.All
            .Where(d => d.Extensions.Any(e => string.Equals(e.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return matches.FirstOrDefault(CanList) ?? matches.FirstOrDefault();
    }

    private static bool CanList(IFormatDescriptor d) => d.Capabilities.HasFlag(FormatCapabilities.CanList);

    public bool IsArchive(string path)
        => DescriptorFor(path) is { } d && CanList(d);

    public string? Identify(string path)
        => DescriptorFor(path)?.DisplayName;

    public Task<IReadOnlyList<ArchiveEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
        {
            var descriptor = DescriptorFor(path) ?? throw new NotSupportedException($"Unrecognised archive: {path}");
            var ops = FormatRegistry.GetArchiveOps(descriptor.Id)
                ?? throw new NotSupportedException($"No archive operations for {descriptor.Id}.");

            using var stream = File.OpenRead(path);
            return ops.List(stream, string.Empty)
                .Select(e => new ArchiveEntry
                {
                    Name = e.Name,
                    IsDirectory = e.IsDirectory,
                    Size = e.OriginalSize,
                    CompressedSize = e.CompressedSize,
                    Modified = e.LastModified is { } dt ? new DateTimeOffset(dt) : null,
                })
                .ToList();
        }, cancellationToken);

    public Task ExtractAllAsync(
        string path, string destinationDir,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var descriptor = DescriptorFor(path) ?? throw new NotSupportedException($"Unrecognised archive: {path}");
            var ops = FormatRegistry.GetArchiveOps(descriptor.Id)
                ?? throw new NotSupportedException($"No archive operations for {descriptor.Id}.");

            List<ArchiveEntryInfo> entries;
            using (var listStream = File.OpenRead(path))
                entries = ops.List(listStream, string.Empty);

            var files = entries.Where(e => !e.IsDirectory).ToList();
            var total = files.Sum(e => Math.Max(0, e.OriginalSize));
            long done = 0;

            foreach (var entry in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var destPath = Path.GetFullPath(Path.Combine(destinationDir, NormalizeEntryName(entry.Name)));
                // Guard against path traversal (zip-slip).
                if (!destPath.StartsWith(Path.GetFullPath(destinationDir), PathComparison))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using (var archiveStream = File.OpenRead(path))
                using (var entryStream = ops.OpenEntry(archiveStream, entry.Name, string.Empty))
                using (var output = File.Create(destPath))
                    entryStream.CopyTo(output);

                done += Math.Max(0, entry.OriginalSize);
                progress?.Report(new OperationProgress(total, done, entry.Name));
            }

            progress?.Report(new OperationProgress(total, total, string.Empty));
        }, cancellationToken);

    public Task ExtractEntryAsync(
        string archivePath, string entryName, string destPath, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var descriptor = DescriptorFor(archivePath) ?? throw new NotSupportedException($"Unrecognised archive: {archivePath}");
            var ops = FormatRegistry.GetArchiveOps(descriptor.Id)
                ?? throw new NotSupportedException($"No archive operations for {descriptor.Id}.");

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var archiveStream = File.OpenRead(archivePath);
            using var entryStream = ops.OpenEntry(archiveStream, entryName, string.Empty);
            using var output = File.Create(destPath);
            entryStream.CopyTo(output);
        }, cancellationToken);

    private static string NormalizeEntryName(string name) =>
        name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
