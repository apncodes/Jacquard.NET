namespace Jacquard.Core;

/// <summary>Base type for streaming events from the agent.</summary>
public abstract record StreamEvent;

public record TextDeltaEvent(string Delta) : StreamEvent;
public record ToolCallStartEvent(string ToolName) : StreamEvent;
public record ToolCallResultEvent(string ToolCallId, ToolResult Result) : StreamEvent;
public record AgentCompleteEvent(AgentResult Result) : StreamEvent;
