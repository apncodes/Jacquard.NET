namespace StrandsAgents.Core;

/// <summary>Request sent to a model provider.</summary>
public record ModelRequest(
    IReadOnlyList<Message> Messages,
    string? SystemPrompt,
    IReadOnlyList<ToolDefinition> Tools,
    ModelParameters Parameters);

/// <summary>Tuning parameters for a model call.</summary>
public record ModelParameters
{
    public int? MaxTokens { get; init; }
    public float? Temperature { get; init; }
    public string? ModelId { get; init; }
}
