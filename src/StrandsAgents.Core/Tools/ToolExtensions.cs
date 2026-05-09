namespace StrandsAgents.Core;

/// <summary>Extension methods for working with tools.</summary>
public static class ToolExtensions
{
    /// <summary>
    /// Wraps an agent as a tool callable by a parent agent.
    /// The sub-agent's InvokeAsync becomes the tool implementation.
    /// </summary>
    public static ITool AsTool(this IAgent agent, string name, string description) =>
        new AgentTool(agent, name, description);
}
