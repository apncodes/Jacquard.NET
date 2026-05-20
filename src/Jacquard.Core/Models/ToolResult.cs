namespace Jacquard.Core;

/// <summary>The result of executing a tool.</summary>
public record ToolResult(string ToolCallId, string Content, bool IsError = false)
{
    /// <summary>Creates a successful result.</summary>
    public static ToolResult Success(string toolCallId, string content) =>
        new(toolCallId, content);

    /// <summary>Creates an error result.</summary>
    public static ToolResult Failure(string toolCallId, string error) =>
        new(toolCallId, error, IsError: true);
}
