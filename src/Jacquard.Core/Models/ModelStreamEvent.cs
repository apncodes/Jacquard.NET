namespace Jacquard.Core;

/// <summary>Base type for streaming events from a model provider.</summary>
public abstract record ModelStreamEvent;

public record TextDeltaModelEvent(string Delta) : ModelStreamEvent;
public record ToolCallStartModelEvent(string Id, string Name) : ModelStreamEvent;
public record ToolCallInputDeltaModelEvent(string Id, string Delta) : ModelStreamEvent;
public record ModelCompleteEvent(ModelResponse Response) : ModelStreamEvent;
