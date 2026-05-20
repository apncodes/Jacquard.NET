using System.Text.Json;

namespace Jacquard.Core;

/// <summary>Wraps an IAgent as an ITool for use in multi-agent hierarchies.</summary>
internal sealed class AgentTool : ITool
{
    private readonly IAgent _agent;

    public AgentTool(IAgent agent, string name, string description)
    {
        _agent = agent;
        var schema = JsonDocument.Parse("""{"type":"object","properties":{"prompt":{"type":"string"}},"required":["prompt"]}""").RootElement.Clone();
        Definition = new ToolDefinition(name, description, schema);
    }

    public ToolDefinition Definition { get; }

    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        var prompt = input.GetProperty("prompt").GetString() ?? string.Empty;
        var result = await _agent.InvokeAsync(prompt, ct).ConfigureAwait(false);
        return ToolResult.Success(Definition.Name, result.Message);
    }
}
