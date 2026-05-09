using StrandsAgents.Core;

namespace StrandsAgents.MultiAgent;

/// <summary>
/// A streaming event emitted by <see cref="PipelineOrchestrator"/> that wraps
/// an underlying <see cref="StreamEvent"/> with its stage context.
/// </summary>
/// <param name="StageIndex">Zero-based index of the pipeline stage that emitted this event.</param>
/// <param name="StageName">Optional descriptive name for the stage.</param>
/// <param name="Event">The underlying stream event from the stage agent.</param>
public record PipelineStageEvent(int StageIndex, string? StageName, StreamEvent Event);
