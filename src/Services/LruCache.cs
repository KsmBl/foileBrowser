namespace FoileBrowser.Services;

/// <summary>
/// A small thread-safe least-recently-used cache. Reads and writes touch an entry, moving it to the
/// front; when the cache is full the least-recently-touched entry is evicted. Used to keep computed
/// folder sizes in memory without unbounded growth (PRD §6.2).
/// </summary>
public sealed class LruCache<TKey, TValue>(int capacity) where TKey : notnull
{
    private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _order = new();
    private readonly Lock _gate = new();

    private readonly record struct Entry(TKey Key, TValue Value);

    public int Count
    {
        get { lock (_gate) return _map.Count; }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                existing.Value = new Entry(key, value);
                _order.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, value));
            _order.AddFirst(node);
            _map[key] = node;

            if (_map.Count > _capacity)
            {
                var lru = _order.Last!;
                _order.RemoveLast();
                _map.Remove(lru.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _order.Clear();
        }
    }
}
