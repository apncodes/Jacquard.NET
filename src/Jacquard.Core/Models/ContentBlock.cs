using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jacquard.Core;

/// <summary>Base type for all message content blocks.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(ToolUseBlock), "tool_use")]
[JsonDerivedType(typeof(ToolResultBlock), "tool_result")]
public abstract record ContentBlock;

/// <summary>Plain text content.</summary>
public record TextBlock(string Text) : ContentBlock;

/// <summary>A tool invocation requested by the model.</summary>
public record ToolUseBlock(string Id, string Name, JsonElement Input) : ContentBlock;

/// <summary>The result of a tool invocation.</summary>
public record ToolResultBlock(string ToolUseId, string Content, bool IsError) : ContentBlock;
