using System.Buffers;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class SecureDeleteService : ISecureDeleteService
{
    private const int BufferSize = 1024 * 1024;

    /// <summary>Running total for one shred call, so nested recursion reports cumulative progress.</summary>
    private sealed class Counter
    {
        public long Written;
    }

    public Task ShredAsync(
        string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default) =>
        ShredEntryAsync(path, progress, new Counter(), cancellationToken);

    private static async Task ShredEntryAsync(
        string path, IProgress<long>? progress, Counter counter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Never follow a symlink into its target: unlink the link itself.
        if (new FileInfo(path).LinkTarget is not null)
        {
            File.Delete(path);
            return;
        }

        if (Directory.Exists(path))
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(path))
                await ShredEntryAsync(child, progress, counter, ct);
            Directory.Delete(path, recursive: false); // now empty
            return;
        }

        if (!File.Exists(path))
            return;

        await OverwriteAsync(path, progress, counter, ct);
        File.Delete(path);
    }

    /// <summary>
    /// Writes zeroes over the whole file and flushes them past the OS cache to the device before the
    /// file is unlinked. A read-only file is made writable first so the pass can't silently be skipped.
    /// </summary>
    private static async Task OverwriteAsync(
        string path, IProgress<long>? progress, Counter counter, CancellationToken ct)
    {
        var attributes = File.GetAttributes(path);
        if (attributes.HasFlag(FileAttributes.ReadOnly))
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            Array.Clear(buffer);
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Write, FileShare.None, BufferSize, FileOptions.WriteThrough);

            var remaining = stream.Length;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = (int)Math.Min(BufferSize, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, chunk), ct);
                remaining -= chunk;
                counter.Written += chunk;
                progress?.Report(counter.Written);
            }

            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
