using System.Text.Json;

namespace StrandsAgents.Core;

/// <summary>A tool invocation requested by the model during a loop iteration.</summary>
public record ToolCall(string Id, string Name, JsonElement Input);
