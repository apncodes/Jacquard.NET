using System.Text.Json;

namespace Jacquard.Core;

/// <summary>A callable tool the agent can invoke.</summary>
public interface ITool
{
    ToolDefinition Definition { get; }
    Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default);
}
