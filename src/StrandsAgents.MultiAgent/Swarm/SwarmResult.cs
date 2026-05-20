using StrandsAgents.Core;

namespace StrandsAgents.MultiAgent;

/// <summary>The result of a completed swarm execution.</summary>
/// <param name="FinalMessage">The final text response produced by the last agent to run.</param>
/// <param name="NodeHistory">Ordered list of every agent invocation that occurred.</param>
/// <param name="Status">How the swarm terminated.</param>
/// <param name="TotalUsage">Aggregate token usage across all agent invocations.</param>
public sealed record SwarmResult(
    string FinalMessage,
    IReadOnlyList<SwarmNodeResult> NodeHistory,
    SwarmStatus Status,
    TokenUsage TotalUsage);

/// <summary>The result of a single agent node invocation within a swarm.</summary>
/// <param name="AgentId">The ID of the agent that produced this result.</param>
/// <param name="Message">The agent's text response.</param>
/// <param name="Usage">Token usage for this invocation.</param>
public sealed record SwarmNodeResult(
    string AgentId,
    string Message,
    TokenUsage Usage);

/// <summary>Describes how a swarm execution terminated.</summary>
public enum SwarmStatus
{
    /// <summary>An agent decided to terminate without handing off.</summary>
    Completed,

    /// <summary>The <see cref="SwarmOrchestrator.MaxHandoffs"/> limit was reached.</summary>
    MaxHandoffsReached,

    /// <summary>The <see cref="SwarmOrchestrator.MaxIterations"/> limit was reached.</summary>
    MaxIterationsReached,

    /// <summary>The overall execution timeout elapsed.</summary>
    TimedOut,
}
