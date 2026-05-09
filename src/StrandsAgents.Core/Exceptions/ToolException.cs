namespace StrandsAgents.Core;

/// <summary>
/// Thrown when a tool invocation fails in a way that should surface directly to the caller,
/// as opposed to being returned to the model as a <see cref="ToolResult"/> with
/// <c>IsError = true</c>.
/// </summary>
public sealed class ToolException : StrandsException
{
    /// <summary>The name of the tool that failed.</summary>
    public string ToolName { get; }

    /// <summary>The model-assigned call ID for the tool invocation that failed.</summary>
    public string ToolCallId { get; }

    /// <summary>
    /// Initializes a new <see cref="ToolException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="toolName">The name of the tool that failed.</param>
    /// <param name="toolCallId">The model-assigned call ID.</param>
    /// <param name="conversationSnapshot">The conversation history at the time of failure, if available.</param>
    /// <param name="inner">The underlying exception, if any.</param>
    public ToolException(
        string message,
        string toolName,
        string toolCallId,
        IReadOnlyList<Message>? conversationSnapshot = null,
        Exception? inner = null)
        : base(message, conversationSnapshot, inner)
    {
        ToolName = toolName;
        ToolCallId = toolCallId;
    }
}
