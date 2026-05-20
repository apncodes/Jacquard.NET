namespace Jacquard.Core;

/// <summary>Execution metrics for an agent invocation.</summary>
public record AgentMetrics(
    TimeSpan TotalLatency,
    int Iterations,
    int ToolCallCount,
    TokenUsage TotalUsage);
