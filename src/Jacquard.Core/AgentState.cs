using System.Collections.Concurrent;

namespace Jacquard.Core;

/// <summary>
/// Thread-safe arbitrary key-value state bag persisted alongside conversation history.
/// Use this to store agent-specific context that survives across invocations.
/// </summary>
public sealed class AgentState
{
    private readonly ConcurrentDictionary<string, object?> _store = new();

    /// <summary>Gets a value by key, returning null if not found or type mismatch.</summary>
    public T? Get<T>(string key)
    {
        if (_store.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    /// <summary>Sets a value by key.</summary>
    public void Set<T>(string key, T value) => _store[key] = value;

    /// <summary>Removes a key. Returns true if it existed.</summary>
    public bool Remove(string key) => _store.TryRemove(key, out _);

    /// <summary>Returns true if the key exists.</summary>
    public bool ContainsKey(string key) => _store.ContainsKey(key);

    /// <summary>Exports state as a read-only dictionary for serialization.</summary>
    public IReadOnlyDictionary<string, object?> ToSnapshot() =>
        new Dictionary<string, object?>(_store);

    /// <summary>Restores state from a snapshot (e.g. loaded from session).</summary>
    public void Restore(IReadOnlyDictionary<string, object?> snapshot)
    {
        _store.Clear();
        foreach (var (key, value) in snapshot)
            _store[key] = value;
    }
}
