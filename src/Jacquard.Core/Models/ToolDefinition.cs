using System.Text.Json;

namespace Jacquard.Core;

/// <summary>Describes a tool to the model (name, description, JSON schema).</summary>
public record ToolDefinition(
    string Name,
    string Description,
    JsonElement InputSchema,
    IReadOnlyDictionary<string, ToolParameterConstraints>? ParameterConstraints = null);
