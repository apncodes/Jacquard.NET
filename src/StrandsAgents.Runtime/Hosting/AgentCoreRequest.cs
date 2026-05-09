using System.Text.Json.Serialization;

namespace StrandsAgents.Runtime.Hosting;

/// <summary>
/// Request payload sent by AgentCore Runtime to <c>POST /invocations</c>.
/// </summary>
public sealed record AgentCoreRequest
{
    /// <summary>The user prompt to pass to the agent.</summary>
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// Session identifier for conversation continuity.
    /// AgentCore Runtime manages session isolation at the infrastructure level.
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; init; }

    /// <summary>Additional context passed by the caller.</summary>
    [JsonPropertyName("context")]
    public Dictionary<string, string>? Context { get; init; }
}
