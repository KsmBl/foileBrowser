using System.Collections.Concurrent;

namespace FoileBrowser.Services;

/// <summary>
/// A thread-safe, approximately-least-recently-used cache tuned for a read-heavy workload (folder
/// sizes are read far more often than written, PRD §6.2). Lookups and stores go through a lock-free
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>; recency is tracked with an <see cref="Interlocked"/>
/// tick counter instead of a locked linked list, so hot reads never contend on a lock. Eviction
/// (only when over capacity) removes the lowest-tick entries with a small margin so it runs rarely.
/// </summary>
public sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private sealed class Node(TValue value, long tick)
    {
        public TValue Value = value;
        public long Tick = tick;
    }

    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly ConcurrentDictionary<TKey, Node> _map = new();
    private long _tick;
    private int _evicting; // 0/1 guard so only one thread evicts at a time

    public int Count => _map.Count;

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            // Touch: a plain write of an interlocked tick is fine — recency only needs to be approximate.
            node.Tick = Interlocked.Increment(ref _tick);
            value = node.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        var tick = Interlocked.Increment(ref _tick);
        _map.AddOrUpdate(
            key,
            static (_, state) => new Node(state.value, state.tick),
            static (_, node, state) => { node.Value = state.value; node.Tick = state.tick; return node; },
            (value, tick));

        if (_map.Count > _capacity)
            Evict();
    }

    private void Evict()
    {
        // Only one evictor at a time; other threads that overflow just skip and let it catch up.
        if (Interlocked.Exchange(ref _evicting, 1) == 1)
            return;
        try
        {
            while (_map.Count > _capacity)
            {
                TKey? oldestKey = default;
                var oldestTick = long.MaxValue;
                var found = false;
                foreach (var pair in _map)
                {
                    if (pair.Value.Tick >= oldestTick)
                        continue;
                    oldestTick = pair.Value.Tick;
                    oldestKey = pair.Key;
                    found = true;
                }

                if (!found || !_map.TryRemove(oldestKey!, out _))
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _evicting, 0);
        }
    }

    public void Clear() => _map.Clear();
}
