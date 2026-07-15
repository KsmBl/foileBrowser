using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class FileOperationService : IFileOperationService
{
    private const int BufferSize = 1 << 20; // 1 MiB copy buffer

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
        => Task.Run(() =>
        {
            Directory.CreateDirectory(destinationDir);

            var total = sources.Sum(MeasureSize);
            long done = 0;

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
                    done += MeasureSize(source);
                    Report(progress, total, done, Path.GetFileName(source));
                    continue;
                }

                if (kind == FileOperationKind.Move && TryFastMove(source, resolved))
                {
                    done += MeasureSize(source);
                    Report(progress, total, done, Path.GetFileName(source));
                    continue;
                }

                CopyRecursive(source, resolved, progress, total, ref done, cancellationToken);

                if (kind == FileOperationKind.Move)
                    DeleteRecursive(source);
            }

            Report(progress, total, total, string.Empty);
        }, cancellationToken);

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

    private static void CopyRecursive(
        string source, string dest, IProgress<OperationProgress>? progress,
        long total, ref long done, CancellationToken cancellationToken)
    {
        if (Directory.Exists(source))
        {
            Directory.CreateDirectory(dest);
            foreach (var child in Directory.EnumerateFileSystemEntries(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var childDest = Path.Combine(dest, Path.GetFileName(child));
                CopyRecursive(child, childDest, progress, total, ref done, cancellationToken);
            }
        }
        else
        {
            CopyFile(source, dest, progress, total, ref done, cancellationToken);
        }
    }

    private static void CopyFile(
        string source, string dest, IProgress<OperationProgress>? progress,
        long total, ref long done, CancellationToken cancellationToken)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);

        var buffer = new byte[BufferSize];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            done += read;
            Report(progress, total, done, Path.GetFileName(source));
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

    private static void Report(IProgress<OperationProgress>? progress, long total, long done, string item)
        => progress?.Report(new OperationProgress(total, done, item));

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
