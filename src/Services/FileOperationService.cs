using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class FileOperationService : IFileOperationService
{
    private readonly Func<CopyOptions> _options;

    public FileOperationService(Func<CopyOptions>? options = null)
        => _options = options ?? (static () => CopyOptions.Default);

    public Task<string> CreateFolderAsync(string parentDir, string name, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var target = UniquePath(Path.Combine(parentDir, name));
            Directory.CreateDirectory(target);
            return target;
        }, cancellationToken);

    public Task<string> CreateFileAsync(string parentDir, string name, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var target = UniquePath(Path.Combine(parentDir, name));
            using (File.Create(target)) { }
            return target;
        }, cancellationToken);

    public Task<string> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            var dir = Path.GetDirectoryName(path)
                ?? throw new IOException($"“{path}” has no parent directory.");
            var target = Path.Combine(dir, newName);

            if (string.Equals(path, target, StringComparison.Ordinal))
                return path;
            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException($"“{newName}” already exists.");

            if (Directory.Exists(path))
                Directory.Move(path, target);
            else
                File.Move(path, target);
            return target;
        }, cancellationToken);

    public Task TransferAsync(
        IReadOnlyList<string> sources,
        string destinationDir,
        FileOperationKind kind,
        IProgress<OperationProgress>? progress,
        Func<ConflictRequest, ConflictResolution> conflictResolver,
        CancellationToken cancellationToken = default)
        => Task.Run(() => TransferCoreAsync(
            sources, destinationDir, kind, progress, conflictResolver, cancellationToken), cancellationToken);

    private async Task TransferCoreAsync(
        IReadOnlyList<string> sources, string destinationDir, FileOperationKind kind,
        IProgress<OperationProgress>? progress, Func<ConflictRequest, ConflictResolution> conflictResolver,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDir);

        var total = sources.Sum(MeasureSize);
        var ctx = new Copier(_options(), progress, total, cancellationToken);

        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destPath = Path.Combine(destinationDir, Path.GetFileName(source.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

            // Guard against moving/copying a directory into itself or a descendant.
            if (Directory.Exists(source) && IsSameOrSubPath(source, destPath))
                throw new IOException($"Cannot transfer “{source}” into itself.");

            var resolved = ResolveDestination(source, destPath, conflictResolver, out var skip);
            if (skip)
            {
                ctx.Skip(MeasureSize(source), Path.GetFileName(source));
                continue;
            }

            if (kind == FileOperationKind.Move && TryFastMove(source, resolved))
            {
                ctx.Skip(MeasureSize(source), Path.GetFileName(source));
                continue;
            }

            await ctx.CopyRecursiveAsync(source, resolved).ConfigureAwait(false);

            if (kind == FileOperationKind.Move)
                DeleteRecursive(source);
        }

        ctx.ReportDone();
    }

    // ---- conflict handling ----

    private static string ResolveDestination(
        string source, string destPath, Func<ConflictRequest, ConflictResolution> resolver, out bool skip)
    {
        skip = false;
        if (!File.Exists(destPath) && !Directory.Exists(destPath))
            return destPath;

        switch (resolver(new ConflictRequest(source, destPath)))
        {
            case ConflictResolution.Overwrite:
                if (File.Exists(destPath)) File.Delete(destPath);
                else if (Directory.Exists(destPath)) Directory.Delete(destPath, recursive: true);
                return destPath;
            case ConflictResolution.Rename:
                return UniquePath(destPath);
            case ConflictResolution.Skip:
                skip = true;
                return destPath;
            default:
                throw new OperationCanceledException("Transfer cancelled at conflict.");
        }
    }

    // ---- copy / move primitives ----

    private static bool TryFastMove(string source, string dest)
    {
        try
        {
            if (Directory.Exists(source))
                Directory.Move(source, dest);
            else
                File.Move(source, dest);
            return true;
        }
        catch (IOException)
        {
            // Cross-volume move (or locked): fall back to copy + delete.
            return false;
        }
    }

    /// <summary>
    /// Walks the source tree and copies files, tracking progress and choosing the byte-moving
    /// strategy (overlapped vs. sequential slurp) per file from the drive profile (PRD §6.3).
    /// </summary>
    private sealed class Copier(
        CopyOptions options, IProgress<OperationProgress>? progress, long total, CancellationToken cancellationToken)
    {
        private long _done;

        /// <summary>
        /// Counts an item that moved without being read block by block — a skipped collision, or a
        /// rename that relocated a whole tree in one call. It has no per-file progress to show, so
        /// the item scale is cleared rather than left reading as the previous file's.
        /// </summary>
        public void Skip(long bytes, string item)
        {
            BeginItem(0);
            Report(bytes, item);
        }

        public void ReportDone() => progress?.Report(new OperationProgress(total, total, string.Empty));

        public async Task CopyRecursiveAsync(string source, string dest)
        {
            if (Directory.Exists(source))
            {
                Directory.CreateDirectory(dest);
                foreach (var child in Directory.EnumerateFileSystemEntries(source))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await CopyRecursiveAsync(child, Path.Combine(dest, Path.GetFileName(child))).ConfigureAwait(false);
                }
            }
            else
            {
                await CopyFileAsync(source, dest).ConfigureAwait(false);
            }
        }

        private async Task CopyFileAsync(string source, string dest)
        {
            var strategy = DriveProfiler.Recommend(source, dest, options);
            var sequential = strategy == CopyStrategy.Sequential;
            var bufferSize = sequential ? options.SequentialBufferSize : options.BufferSize;

            var readHint = FileOptions.Asynchronous | (sequential ? FileOptions.SequentialScan : FileOptions.None);
            await using var input = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, readHint);
            await using var output = new FileStream(
                dest, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous);

            var name = Path.GetFileName(source);
            BeginItem(input.Length);
            if (sequential)
                await CopySequentialAsync(input, output, bufferSize, name).ConfigureAwait(false);
            else
                await CopyOverlappedAsync(input, output, bufferSize, name).ConfigureAwait(false);
        }

        // Read a full block, then write it. No concurrent read+write, so a single mechanical/optical
        // head never seeks back and forth between the source and destination regions.
        private async Task CopySequentialAsync(FileStream input, FileStream output, int bufferSize, string name)
        {
            var buffer = new byte[bufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                Report(read, name);
            }
        }

        // Double-buffered: while the current block is being written, the next block is already being
        // read into the other buffer, so read and write overlap (best on SSD / cross-device).
        private async Task CopyOverlappedAsync(FileStream input, FileStream output, int bufferSize, string name)
        {
            var readBuf = new byte[bufferSize];
            var writeBuf = new byte[bufferSize];

            var read = await input.ReadAsync(readBuf.AsMemory(0, bufferSize), cancellationToken).ConfigureAwait(false);
            while (read > 0)
            {
                // Swap: writeBuf now holds the freshly-read data; readBuf is free for the next block.
                (readBuf, writeBuf) = (writeBuf, readBuf);

                var writeTask = output.WriteAsync(writeBuf.AsMemory(0, read), cancellationToken);
                var nextRead = input.ReadAsync(readBuf.AsMemory(0, bufferSize), cancellationToken);

                await writeTask.ConfigureAwait(false);
                Report(read, name);
                read = await nextRead.ConfigureAwait(false);
            }
        }

        /// <summary>The file currently being copied, so each block can report its own share too.</summary>
        private long _itemTotal;
        private long _itemDone;

        /// <summary>Starts a new current file: its size is known before the first block moves.</summary>
        private void BeginItem(long size)
        {
            _itemTotal = size;
            _itemDone = 0;
        }

        private void Report(long bytes, string item)
        {
            _done += bytes;
            _itemDone += bytes;
            progress?.Report(new OperationProgress(total, _done, item, _itemTotal, _itemDone));
        }
    }

    private static void DeleteRecursive(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }

    // ---- helpers ----

    private static long MeasureSize(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => SafeLength(f));
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try { return file.Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Appends " (n)" before the extension until the path is free.</summary>
    internal static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
    }

    private static bool IsSameOrSubPath(string ancestor, string candidate)
    {
        var a = Path.GetFullPath(ancestor).TrimEnd(Path.DirectorySeparatorChar);
        var c = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(a, c, PathComparison)
            || c.StartsWith(a + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
