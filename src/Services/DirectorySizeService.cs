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

    public bool TryGetCached(string path, out long size) => _cache.TryGet(path, out size);

    public async Task<long> GetSizeAsync(
        string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGet(path, out var cached))
        {
            progress?.Report(cached);
            return cached;
        }

        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check: another scheduler may have computed this while we waited for a slot.
            if (_cache.TryGet(path, out cached))
            {
                progress?.Report(cached);
                return cached;
            }

            var size = await Task.Run(() => Measure(path, progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            _cache.Set(path, size);
            return size;
        }
        finally
        {
            _throttle.Release();
        }
    }

    // Iterative depth-first walk so deep trees can't blow the stack; reports the running total
    // periodically so the UI can show live per-folder progress without flooding it.
    private static long Measure(string root, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        long total = 0;
        long sinceReport = 0;
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = stack.Pop();

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
                    try { total += new FileInfo(entry).Length; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

                    // Report roughly every 4 MiB of progress to keep UI churn bounded.
                    sinceReport = total - sinceReport > (4 << 20) ? Report(progress, total) : sinceReport;
                }
            }
        }

        progress?.Report(total);
        return total;
    }

    private static long Report(IProgress<long>? progress, long total)
    {
        progress?.Report(total);
        return total;
    }
}
