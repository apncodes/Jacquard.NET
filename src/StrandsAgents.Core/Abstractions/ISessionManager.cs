namespace StrandsAgents.Core;

/// <summary>Persists and restores agent sessions across process restarts.</summary>
public interface ISessionManager
{
    Task<AgentSession?> LoadAsync(string sessionId, CancellationToken ct = default);
    Task SaveAsync(string sessionId, AgentSession session, CancellationToken ct = default);
}
