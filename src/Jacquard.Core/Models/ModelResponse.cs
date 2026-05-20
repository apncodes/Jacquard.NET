namespace Jacquard.Core;

/// <summary>Response from a model provider.</summary>
public record ModelResponse(
    string? TextContent,
    IReadOnlyList<ToolCall> ToolCalls,
    StopReason StopReason,
    TokenUsage Usage);
