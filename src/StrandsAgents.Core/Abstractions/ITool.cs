using System.Text.Json;

namespace StrandsAgents.Core;

/// <summary>A callable tool the agent can invoke.</summary>
public interface ITool
{
    ToolDefinition Definition { get; }
    Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default);
}
