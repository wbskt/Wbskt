using System.Collections.Concurrent;

namespace Wbskt.Common.Utilities;

public class ConcurrentKeys<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, bool> _dictionary = new();

    public ConcurrentKeys()
    {
    }

    public ConcurrentKeys(T[] keys)
    {
        foreach (var key in keys) Add(key);
    }

    public void Add(T key)
    {
        _dictionary.TryAdd(key, false);
    }

    public void Remove(T key)
    {
        _dictionary.TryRemove(key, out _);
    }

    public bool Contains(T key)
    {
        return _dictionary.ContainsKey(key);
    }

    public void Clear()
    {
        _dictionary.Clear();
    }

    public ICollection<T> GetKeys()
    {
        return _dictionary.Keys;
    }
}
