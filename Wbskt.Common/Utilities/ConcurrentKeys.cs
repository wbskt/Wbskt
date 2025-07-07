using System.Collections.Concurrent;

namespace Wbskt.Common.Utilities;

public class ConcurrentKeys<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, bool> dictionary = new();

    public ConcurrentKeys()
    {
    }

    public ConcurrentKeys(T[] keys)
    {
        foreach (var key in keys) Add(key);
    }

    public void Add(T key)
    {
        dictionary.TryAdd(key, default);
    }

    public void Remove(T key)
    {
        dictionary.TryRemove(key, out _);
    }

    public bool Contains(T key)
    {
        return dictionary.ContainsKey(key);
    }

    public void Clear()
    {
        dictionary.Clear();
    }

    public ICollection<T> GetKeys()
    {
        return dictionary.Keys;
    }
}
