namespace StrandsAgents.Core;

/// <summary>Base type for all hook events fired by the agent event loop.</summary>
public abstract record HookEvent;

/// <summary>Fired before each model call. Set <see cref="Interrupt"/> to <c>true</c> to halt the agent loop.</summary>
public record BeforeModelCallEvent(ModelRequest Request) : HookEvent
{
    /// <summary>When set to <c>true</c>, the agent loop will halt after this hook completes.</summary>
    public bool Interrupt { get; set; }
}

/// <summary>Fired after each model call.</summary>
public record AfterModelCallEvent(ModelRequest Request, ModelResponse Response) : HookEvent;

/// <summary>Fired before each tool call. Set <see cref="Interrupt"/> to <c>true</c> to halt the agent loop.</summary>
public record BeforeToolCallEvent(ToolCall Call) : HookEvent
{
    /// <summary>When set to <c>true</c>, the agent loop will halt after this hook completes.</summary>
    public bool Interrupt { get; set; }
}

/// <summary>Fired after each tool call.</summary>
public record AfterToolCallEvent(ToolCall Call, ToolResult Result) : HookEvent;
