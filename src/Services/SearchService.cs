using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FoileBrowser.Models;

namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class SearchService : ISearchService
{
    public async IAsyncEnumerable<FileSystemEntry> SearchAsync(
        string rootPath,
        string query,
        IReadOnlyCollection<string>? extensions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<FileSystemEntry>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var exts = extensions is { Count: > 0 }
            ? new HashSet<string>(extensions.Select(e => e.TrimStart('.').ToLowerInvariant()))
            : null;

        // Produce hits on a background thread; the async iterator consumes them on the caller's context.
        var producer = Task.Run(() =>
        {
            try
            {
                Walk(rootPath, query, exts, channel.Writer, cancellationToken);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
                yield return entry;
        }
        finally
        {
            // Surface producer faults (other than the expected cancellation) and ensure it ended.
            await producer.ConfigureAwait(false);
        }
    }

    private static void Walk(
        string root, string query, HashSet<string>? exts,
        ChannelWriter<FileSystemEntry> writer, CancellationToken cancellationToken)
    {
        // Explicit stack DFS avoids deep recursion on large trees.
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            IEnumerable<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(dir).EnumerateFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue; // skip directories we cannot read
            }

            foreach (var info in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isDir = (info.Attributes & FileAttributes.Directory) == FileAttributes.Directory;

                if (isDir)
                    stack.Push(info.FullName);

                if (!IsHit(info, isDir, query, exts))
                    continue;

                writer.TryWrite(ToEntry(info, isDir));
            }
        }
    }

    private static bool IsHit(FileSystemInfo info, bool isDir, string query, HashSet<string>? exts)
    {
        if (exts is not null)
        {
            if (isDir)
                return false; // extension filter targets files only
            var ext = Path.GetExtension(info.Name).TrimStart('.').ToLowerInvariant();
            if (!exts.Contains(ext))
                return false;
        }

        return FuzzyMatcher.IsMatch(query, info.Name);
    }

    private static FileSystemEntry ToEntry(FileSystemInfo info, bool isDir)
    {
        long? size = null;
        DateTimeOffset? modified = null;
        try { modified = info.LastWriteTime; } catch { /* ignore */ }
        try { if (info is FileInfo f) size = f.Length; } catch { /* ignore */ }

        var isHidden = (info.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden
            || info.Name.StartsWith('.');

        return new FileSystemEntry
        {
            Name = info.Name,
            FullPath = info.FullName,
            Kind = isDir ? FileSystemEntryKind.Directory : FileSystemEntryKind.File,
            Size = size,
            Modified = modified,
            IsHidden = isHidden,
        };
    }
}
