using System.Text.Json;

namespace Jacquard.Core;

/// <summary>A tool invocation requested by the model during a loop iteration.</summary>
public record ToolCall(string Id, string Name, JsonElement Input);
