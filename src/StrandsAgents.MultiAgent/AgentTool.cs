using StrandsAgents.Core;
using System.Text.Json;

namespace StrandsAgents.MultiAgent;

/// <summary>
/// Wraps an <see cref="IAgent"/> as an <see cref="ITool"/> so it can be registered in a
/// <see cref="ToolRegistry"/> and called by a parent agent as a tool in multi-agent hierarchies.
/// </summary>
/// <remarks>
/// The wrapped agent is invoked with the value of the <c>prompt</c> property from the
/// tool's JSON input. Its <see cref="AgentResult.Message"/> is returned as the tool result.
/// </remarks>
public sealed class AgentTool : ITool
{
    private readonly IAgent _agent;

    /// <summary>
    /// Initializes a new <see cref="AgentTool"/>.
    /// </summary>
    /// <param name="agent">The agent to wrap.</param>
    /// <param name="name">Tool name as it will appear to the calling model.</param>
    /// <param name="description">Tool description used in the model's tool spec.</param>
    public AgentTool(IAgent agent, string name, string description)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);

        _agent = agent;

        var schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "prompt": { "type": "string", "description": "The task or question to send to this agent." }
              },
              "required": ["prompt"]
            }
            """).RootElement.Clone();

        Definition = new ToolDefinition(name, description, schema);
    }

    /// <inheritdoc/>
    public ToolDefinition Definition { get; }

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("prompt", out var promptEl) ||
            promptEl.GetString() is not { } prompt)
            return ToolResult.Failure(Definition.Name, "Missing required field: prompt.");

        var result = await _agent.InvokeAsync(prompt, ct).ConfigureAwait(false);
        return ToolResult.Success(Definition.Name, result.Message);
    }
}
