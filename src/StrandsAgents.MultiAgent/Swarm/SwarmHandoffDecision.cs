namespace StrandsAgents.MultiAgent;

/// <summary>
/// Structured routing decision extracted from an agent's response via
/// <c>GetStructuredOutputAsync&lt;SwarmHandoffDecision&gt;</c>.
/// </summary>
/// <param name="NextAgentId">
/// The ID of the agent to hand off to, or <see langword="null"/> to terminate the swarm
/// and return <see cref="Message"/> as the final answer.
/// </param>
/// <param name="Message">
/// Instructions for the next agent when handing off, or the final answer when terminating.
/// </param>
/// <param name="Context">
/// Optional key-value pairs contributed to the swarm's shared knowledge store.
/// Keys are namespaced automatically as <c>{agentId}.{key}</c>.
/// </param>
internal sealed record SwarmHandoffDecision(
    string? NextAgentId,
    string Message,
    Dictionary<string, string>? Context);
