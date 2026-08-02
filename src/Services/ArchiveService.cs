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
        => DescriptorForExtension(Path.GetExtension(path).TrimStart('.'));

    private static IFormatDescriptor? DescriptorForExtension(string extension)
    {
        _ = Registration.Value;
        if (string.IsNullOrEmpty(extension))
            return null;

        // Scan the registry directly rather than trusting GetByExtension, whose extension index can
        // return a non-matching descriptor once every format is registered (e.g. resolving ".zip" to
        // the VDI reader). Prefer a descriptor that both claims the extension and can list entries.
        var matches = FormatRegistry.All
            .Where(d => d.Extensions.Any(e => string.Equals(e.TrimStart('.'), extension, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return matches.FirstOrDefault(CanList) ?? matches.FirstOrDefault();
    }

    private static bool CanList(IFormatDescriptor d) => d.Capabilities.HasFlag(FormatCapabilities.CanList);

    // ---- compressed tarballs -------------------------------------------------------------------

    /// <summary>
    /// The names that mean "a tar inside a single-stream compressor", and the compressor each one
    /// names.
    /// </summary>
    /// <remarks>
    /// gzip, bzip2, xz and zstd compress one unnamed stream; none of them has a directory, so none
    /// of them can list anything and the registry rightly says so. That answer is right about the
    /// format and wrong about the file: on Unix a tarball is how software is shipped, and
    /// <c>.tar.gz</c> was simply not enterable — which reads as "tar files do not open", since the
    /// plain <c>.tar</c> that does work is the rarer thing to actually have.
    /// <para>
    /// Matched on the whole suffix rather than the last extension, because the last extension of
    /// <c>foo.tar.gz</c> is <c>.gz</c> and says nothing about the tar; and <c>.tgz</c> and its
    /// siblings are contractions that no format claims at all.
    /// </para>
    /// </remarks>
    private static readonly (string Suffix, string Compressor)[] Tarballs =
    [
        (".tar.gz", "gz"), (".tgz", "gz"),
        (".tar.bz2", "bz2"), (".tbz2", "bz2"), (".tbz", "bz2"),
        (".tar.xz", "xz"), (".txz", "xz"),
        (".tar.zst", "zst"), (".tzst", "zst"),
        (".tar.lzma", "lzma"), (".tar.lz", "lz"), (".tar.z", "z"),
    ];

    /// <summary>The compressor wrapping this path's tar, or null if the name is not a tarball.</summary>
    private static IFormatDescriptor? TarballCompressor(string path)
    {
        foreach (var (suffix, compressor) in Tarballs)
            if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return DescriptorForExtension(compressor);

        return null;
    }

    /// <summary>The stream operations that unwrap this path, or null if it is not a tarball.</summary>
    private static IStreamFormatOperations? TarballDecompressor(string path)
        => TarballCompressor(path) is { } outer ? FormatRegistry.GetStreamOps(outer.Id) : null;

    /// <summary>How to read a given path: the operations to use, and how to get at the bytes.</summary>
    /// <remarks>
    /// One seam for both shapes. A plain archive is its own file; a tarball is the tar that comes out
    /// of the compressor, read straight through the decompressing wrapper — tar is sequential, so
    /// there is nothing to seek back to and no temporary copy to make.
    /// </remarks>
    private static (IArchiveFormatOperations Ops, Func<Stream> Open, string Name)? ReaderFor(string path)
    {
        if (TarballDecompressor(path) is { } decompressor && DescriptorForExtension("tar") is { } tar
            && FormatRegistry.GetArchiveOps(tar.Id) is { } tarOps)
            return (tarOps, () => decompressor.WrapDecompress(File.OpenRead(path)) ?? File.OpenRead(path), tar.DisplayName);

        if (DescriptorFor(path) is { } descriptor && CanList(descriptor)
            && FormatRegistry.GetArchiveOps(descriptor.Id) is { } ops)
            return (ops, () => File.OpenRead(path), descriptor.DisplayName);

        return null;
    }

    public bool IsArchive(string path) => ReaderFor(path) is not null;

    public string? Identify(string path)
        => TarballCompressor(path) is { } outer
            ? $"{outer.DisplayName} (tar)"   // ".tgz" names no format of its own, so say what it holds
            : DescriptorFor(path)?.DisplayName;

    public Task<IReadOnlyList<ArchiveEntry>> ListAsync(string path, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
        {
            var reader = ReaderFor(path) ?? throw new NotSupportedException($"Unrecognised archive: {path}");

            using var stream = reader.Open();
            return reader.Ops.List(stream, string.Empty)
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
            var reader = ReaderFor(path) ?? throw new NotSupportedException($"Unrecognised archive: {path}");

            List<ArchiveEntryInfo> entries;
            using (var listStream = reader.Open())
                entries = reader.Ops.List(listStream, string.Empty);

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

                using (var archiveStream = reader.Open())
                using (var entryStream = reader.Ops.OpenEntry(archiveStream, entry.Name, string.Empty))
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
            var reader = ReaderFor(archivePath) ?? throw new NotSupportedException($"Unrecognised archive: {archivePath}");

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var archiveStream = reader.Open();
            using var entryStream = reader.Ops.OpenEntry(archiveStream, entryName, string.Empty);
            using var output = File.Create(destPath);
            entryStream.CopyTo(output);
        }, cancellationToken);

    private static string NormalizeEntryName(string name) =>
        name.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
