namespace StrandsAgents.Core;

/// <summary>The result of a completed agent invocation.</summary>
public record AgentResult(
    string Message,
    StopReason StopReason,
    TokenUsage Usage,
    AgentMetrics Metrics);
