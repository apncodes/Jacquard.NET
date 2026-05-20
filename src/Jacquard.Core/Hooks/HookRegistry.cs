using System.Collections.Concurrent;

namespace Jacquard.Core;

/// <summary>
/// Thread-safe registry for hook event handlers. Handlers are invoked in registration order (FIFO).
/// </summary>
public sealed class HookRegistry
{
    private readonly ConcurrentDictionary<Type, List<Func<HookEvent, Task>>> _handlers = new();
    private readonly object _lock = new();

    /// <summary>Registers a handler for the specified event type.</summary>
    public void Register<TEvent>(Func<TEvent, Task> handler) where TEvent : HookEvent
    {
        var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Func<HookEvent, Task>>());
        lock (_lock)
        {
            list.Add(evt => handler((TEvent)evt));
        }
    }

    /// <summary>
    /// Fires all handlers registered for <typeparamref name="TEvent"/> sequentially in registration order.
    /// If a handler throws, the exception propagates immediately to the caller.
    /// </summary>
    public async Task FireAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : HookEvent
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var list))
            return;

        List<Func<HookEvent, Task>> snapshot;
        lock (_lock)
        {
            snapshot = new List<Func<HookEvent, Task>>(list);
        }

        foreach (var handler in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            await handler(evt).ConfigureAwait(false);
        }
    }
}
