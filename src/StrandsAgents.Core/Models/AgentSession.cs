namespace StrandsAgents.Core;

/// <summary>Persisted agent session — conversation history + state.</summary>
public record AgentSession(
    string SessionId,
    IReadOnlyList<Message> Messages,
    IReadOnlyDictionary<string, object?> State,
    DateTimeOffset LastUpdated);
