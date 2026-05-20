using StrandsAgents.Core;

namespace StrandsAgents.MultiAgent;

/// <summary>
/// A named agent node registered in a <see cref="SwarmOrchestrator"/>.
/// </summary>
/// <param name="Id">
/// Unique identifier for this node. Used in handoff decisions and node history.
/// </param>
/// <param name="Agent">The agent to execute at this node.</param>
/// <param name="Description">
/// Optional human-readable description of this agent's specialisation.
/// Included in the shared context so other agents can make informed handoff decisions.
/// </param>
public sealed record SwarmAgentNode(
    string Id,
    IAgent Agent,
    string? Description = null);
