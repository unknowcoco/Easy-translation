namespace EasyTranslation.Translation;

/// <summary>Very small thread-safe LRU cache keyed by a string (SHA-256 of the request).</summary>
public sealed class LruCache
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<KeyValuePair<string, string>>> _map = new();
    private readonly LinkedList<KeyValuePair<string, string>> _order = new();

    public LruCache(int capacity = 100)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool TryGet(string key, out string value)
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

        value = string.Empty;
        return false;
    }

    public void Set(string key, string value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = _order.AddFirst(new KeyValuePair<string, string>(key, value));
            _map[key] = node;
            while (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
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
