using System.Collections.Concurrent;

namespace StrandsAgents.Core;

/// <summary>
/// In-memory implementation of <see cref="ISessionManager"/>.
/// Session data is not persisted across process restarts. Suitable for development
/// and testing; use a durable implementation for production.
/// </summary>
public sealed class InMemorySessionManager : ISessionManager
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

    /// <inheritdoc/>
    public Task<AgentSession?> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    /// <inheritdoc/>
    public Task SaveAsync(string sessionId, AgentSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(session);
        _sessions[sessionId] = session;
        return Task.CompletedTask;
    }
}
