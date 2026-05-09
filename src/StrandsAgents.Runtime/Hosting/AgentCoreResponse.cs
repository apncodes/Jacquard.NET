using System.Text.Json.Serialization;

namespace StrandsAgents.Runtime.Hosting;

/// <summary>Response returned by <c>POST /invocations</c> to AgentCore Runtime.</summary>
public sealed record AgentCoreResponse
{
    /// <summary>The agent's final response text.</summary>
    [JsonPropertyName("result")]
    public string Result { get; init; } = string.Empty;

    /// <summary>Reason the agent stopped (e.g. "end_turn", "max_tokens").</summary>
    [JsonPropertyName("stopReason")]
    public string StopReason { get; init; } = string.Empty;

    /// <summary>Token usage for the invocation, if available.</summary>
    [JsonPropertyName("usage")]
    public AgentCoreUsage? Usage { get; init; }
}

/// <summary>Token usage counts for an AgentCore invocation.</summary>
public sealed record AgentCoreUsage
{
    /// <summary>Number of input tokens consumed.</summary>
    [JsonPropertyName("inputTokens")]
    public int InputTokens { get; init; }

    /// <summary>Number of output tokens produced.</summary>
    [JsonPropertyName("outputTokens")]
    public int OutputTokens { get; init; }
}
