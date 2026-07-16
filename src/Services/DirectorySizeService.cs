namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class DirectorySizeService : IDirectorySizeService
{
    private readonly LruCache<string, long> _cache;
    private readonly SemaphoreSlim _throttle;

    public DirectorySizeService(int cacheCapacity = 4096, int maxConcurrency = 0)
    {
        _cache = new LruCache<string, long>(cacheCapacity);
        var workers = maxConcurrency > 0 ? maxConcurrency : Math.Max(2, Environment.ProcessorCount / 2);
        _throttle = new SemaphoreSlim(workers, workers);
    }

    public bool TryGetCached(string path, out long size) => _cache.TryGet(Key(path), out size);

    public async Task<long> GetSizeAsync(
        string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var key = Key(path);
        if (_cache.TryGet(key, out var cached))
        {
            progress?.Report(cached);
            return cached;
        }

        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check: another scheduler may have computed this while we waited for a slot.
            if (_cache.TryGet(key, out cached))
            {
                progress?.Report(cached);
                return cached;
            }

            return await Task.Run(() => MeasureTree(key, progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>
    /// Walks the whole subtree once and caches the total for <paramref name="root"/> <em>and every
    /// descendant directory</em>. That way, after a folder's size is known, drilling into any folder
    /// inside it is instant instead of restarting the calculation (PRD §6.2).
    /// </summary>
    private long MeasureTree(string root, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // Files summed per directory; parents fold in their children's totals in a post-order pass.
        var acc = new Dictionary<string, long>(StringComparer.Ordinal);
        var discovered = new List<string>();
        var stack = new Stack<string>();
        stack.Push(root);

        long running = 0;
        long lastReport = 0;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            discovered.Add(dir);
            acc[dir] = 0;

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue; // unreadable subtree contributes nothing
            }

            foreach (var entry in entries)
            {
                if (Directory.Exists(entry))
                {
                    stack.Push(entry);
                }
                else
                {
                    long length;
                    try { length = new FileInfo(entry).Length; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { length = 0; }

                    acc[dir] += length;
                    running += length;
                    if (running - lastReport > (4 << 20)) // report the running total every ~4 MiB
                    {
                        progress?.Report(running);
                        lastReport = running;
                    }
                }
            }
        }

        // Post-order: parents are discovered before children, so folding totals back in reverse
        // discovery order guarantees a directory's own files + all descendants are summed before it.
        for (var i = discovered.Count - 1; i >= 0; i--)
        {
            var dir = discovered[i];
            var total = acc[dir];
            _cache.Set(dir, total);

            if (Path.GetDirectoryName(dir) is { } parent && acc.ContainsKey(parent))
                acc[parent] += total;
        }

        var rootTotal = acc[root];
        progress?.Report(rootTotal);
        return rootTotal;
    }

    // Normalise so cache keys are stable regardless of a trailing separator.
    private static string Key(string path) =>
        string.IsNullOrEmpty(path) ? path : Path.TrimEndingDirectorySeparator(path);
}
