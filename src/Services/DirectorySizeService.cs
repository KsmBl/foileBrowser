namespace FoileBrowser.Services;

/// <inheritdoc />
public sealed class DirectorySizeService : IDirectorySizeService
{
    // Hard caps so sizing a huge or pathological tree can never blow up RAM/CPU (PRD §6.2/§6.12).
    private const int MaxEntries = 400_000;   // stop after this many files+dirs; report a partial size
    private const long ReportEvery = 8 << 20; // push a running-total update roughly every 8 MiB

    // Linux pseudo-filesystems report bogus/huge sizes (e.g. /proc/kcore) and cyclic trees — never size them.
    private static readonly string[] PseudoRoots =
        OperatingSystem.IsLinux() ? ["/proc", "/sys", "/dev", "/run"] : [];

    private readonly LruCache<string, long> _cache;
    private readonly SemaphoreSlim _throttle;

    public DirectorySizeService(int cacheCapacity = 512, int maxConcurrency = 0)
    {
        _cache = new LruCache<string, long>(cacheCapacity);
        var workers = maxConcurrency > 0 ? maxConcurrency : Math.Max(1, Environment.ProcessorCount / 2);
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

        // Don't even schedule a walk for pseudo filesystems — their sizes are meaningless.
        if (IsPseudo(key))
        {
            _cache.Set(key, 0);
            progress?.Report(0);
            return 0;
        }

        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGet(key, out cached))
            {
                progress?.Report(cached);
                return cached;
            }

            var (size, complete) = await Task
                .Run(() => Measure(key, progress, cancellationToken), cancellationToken)
                .ConfigureAwait(false);

            // Only cache a complete result; a partial (capped) size would otherwise stick as wrong.
            if (complete)
                _cache.Set(key, size);
            return size;
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>
    /// Iterative DFS that sums bytes without ever holding the whole subtree in memory: it keeps only
    /// the frontier stack plus one subtotal per <em>immediate</em> child (so drilling one level in is
    /// instant). Symlinks/reparse points are never followed (avoids cycles and huge link targets),
    /// pseudo-filesystems are skipped, and the walk stops at <see cref="MaxEntries"/>.
    /// </summary>
    private (long Size, bool Complete) Measure(string root, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        long total = 0;
        long lastReport = 0;
        var visited = 0;
        var complete = true;

        // Cache one subtotal per immediate child of the root; bounded by the number of children.
        var childTotals = new Dictionary<string, long>(StringComparer.Ordinal);
        var stack = new Stack<(string Dir, string? Bucket)>();
        stack.Push((root, null));

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (dir, bucket) = stack.Pop();

            IEnumerable<FileSystemInfo> infos;
            try
            {
                infos = new DirectoryInfo(dir).EnumerateFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var info in infos)
            {
                if (visited++ >= MaxEntries)
                {
                    complete = false;
                    break;
                }

                // Never follow symlinks/junctions — they cause cycles and can point at huge targets.
                if (info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if (info is DirectoryInfo)
                {
                    if (IsPseudo(info.FullName))
                        continue;
                    // A direct child of the root becomes its own subtotal bucket.
                    stack.Push((info.FullName, bucket ?? info.FullName));
                }
                else
                {
                    long length;
                    try { length = ((FileInfo)info).Length; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { length = 0; }

                    total += length;
                    if (bucket is not null)
                        childTotals[bucket] = childTotals.GetValueOrDefault(bucket) + length;

                    if (total - lastReport > ReportEvery)
                    {
                        progress?.Report(total);
                        lastReport = total;
                    }
                }
            }

            if (!complete)
                break;
        }

        // Cache immediate children only when the walk finished (otherwise their subtotals are partial).
        if (complete)
            foreach (var (child, size) in childTotals)
                _cache.Set(child, size);

        progress?.Report(total);
        return (total, complete);
    }

    private static bool IsPseudo(string path) =>
        PseudoRoots.Any(root => path == root || path.StartsWith(root + "/", StringComparison.Ordinal));

    // Normalise so cache keys are stable regardless of a trailing separator.
    private static string Key(string path) =>
        string.IsNullOrEmpty(path) ? path : Path.TrimEndingDirectorySeparator(path);
}
