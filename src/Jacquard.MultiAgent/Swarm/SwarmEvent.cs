using Jacquard.Core;

namespace Jacquard.MultiAgent;

/// <summary>Base type for all events emitted by <see cref="SwarmOrchestrator.StreamAsync"/>.</summary>
public abstract record SwarmEvent;

/// <summary>
/// Emitted when the swarm starts executing. Contains the initial task and the entry-point agent ID.
/// </summary>
public sealed record SwarmStartedEvent(
    string Task,
    string EntryAgentId) : SwarmEvent;

/// <summary>
/// Emitted just before an agent node begins executing.
/// </summary>
/// <param name="AgentId">The ID of the agent about to run.</param>
/// <param name="Description">The agent's optional description.</param>
/// <param name="HandoffMessage">
/// The message passed from the previous agent, or <see langword="null"/> for the entry point.
/// </param>
/// <param name="Iteration">One-based iteration counter across all agent invocations.</param>
public sealed record AgentStartedEvent(
    string AgentId,
    string? Description,
    string? HandoffMessage,
    int Iteration) : SwarmEvent;

/// <summary>
/// A streaming text token emitted by the currently active agent.
/// Allows the caller to display agent output in real time.
/// </summary>
/// <param name="AgentId">The agent that produced this token.</param>
/// <param name="Delta">The incremental text fragment.</param>
public sealed record AgentTextDeltaEvent(
    string AgentId,
    string Delta) : SwarmEvent;

/// <summary>
/// Emitted when the currently active agent calls a tool.
/// </summary>
/// <param name="AgentId">The agent invoking the tool.</param>
/// <param name="ToolName">The name of the tool being called.</param>
public sealed record AgentToolCallEvent(
    string AgentId,
    string ToolName) : SwarmEvent;

/// <summary>
/// Emitted when a tool call completes and the result is available.
/// </summary>
/// <param name="AgentId">The agent that invoked the tool.</param>
/// <param name="ToolCallId">The tool call identifier.</param>
/// <param name="Result">The tool result.</param>
public sealed record AgentToolResultEvent(
    string AgentId,
    string ToolCallId,
    ToolResult Result) : SwarmEvent;

/// <summary>
/// Emitted when an agent finishes its invocation.
/// </summary>
/// <param name="AgentId">The agent that completed.</param>
/// <param name="Result">The agent's full result including message and token usage.</param>
public sealed record AgentCompletedEvent(
    string AgentId,
    AgentResult Result) : SwarmEvent;

/// <summary>
/// Emitted when the routing agent decides to hand off to another agent.
/// </summary>
/// <param name="FromAgentId">The agent handing off.</param>
/// <param name="ToAgentId">The agent receiving control.</param>
/// <param name="Message">The handoff message / instructions for the next agent.</param>
public sealed record HandoffEvent(
    string FromAgentId,
    string ToAgentId,
    string Message) : SwarmEvent;

/// <summary>
/// Emitted when the swarm terminates — either by agent decision or a safety bound.
/// </summary>
/// <param name="Status">How the swarm terminated.</param>
/// <param name="FinalMessage">The final text response.</param>
/// <param name="NodeHistory">Ordered list of every agent invocation.</param>
/// <param name="TotalUsage">Aggregate token usage across all invocations.</param>
public sealed record SwarmCompletedEvent(
    SwarmStatus Status,
    string FinalMessage,
    IReadOnlyList<SwarmNodeResult> NodeHistory,
    TokenUsage TotalUsage) : SwarmEvent;
