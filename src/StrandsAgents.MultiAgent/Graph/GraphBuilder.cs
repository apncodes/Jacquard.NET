using StrandsAgents.Core;

namespace StrandsAgents.MultiAgent;

/// <summary>
/// Builds an <see cref="AgentGraph"/> by registering nodes (agents) and edges (transitions).
/// </summary>
public sealed class GraphBuilder
{
    /// <summary>Sentinel value returned by a conditional edge selector to terminate graph execution.</summary>
    public const string End = "__end__";

    /// <summary>Sentinel value representing the implicit start of the graph.</summary>
    public const string Start = "__start__";

    private readonly Dictionary<string, IAgent> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _edges = new(StringComparer.Ordinal);
    private string? _startNode;

    /// <summary>Registers a named agent node in the graph.</summary>
    /// <param name="name">Unique node name.</param>
    /// <param name="agent">The agent to execute at this node.</param>
    /// <exception cref="ArgumentException">Thrown when a node with the same name already exists.</exception>
    public GraphBuilder AddNode(string name, IAgent agent)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(agent);

        if (_nodes.ContainsKey(name))
            throw new ArgumentException($"A node named '{name}' is already registered.", nameof(name));

        _nodes[name] = agent;
        _startNode ??= name;
        return this;
    }

    /// <summary>Adds an unconditional edge from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public GraphBuilder AddEdge(string from, string to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        _edges[from] = new UnconditionalEdge(to);
        return this;
    }

    /// <summary>
    /// Adds a conditional edge from <paramref name="from"/> whose target is determined at runtime
    /// by <paramref name="selector"/>. Return <see cref="End"/> to terminate execution.
    /// </summary>
    public GraphBuilder AddConditionalEdge(string from, Func<AgentResult, string> selector)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(selector);

        _edges[from] = new ConditionalEdge(selector);
        return this;
    }

    /// <summary>Validates the graph and returns an <see cref="AgentGraph"/> ready to run.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the graph has no nodes or edge targets are unregistered.</exception>
    public AgentGraph Build()
    {
        if (_nodes.Count == 0)
            throw new InvalidOperationException("The graph must contain at least one node.");

        // Validate all edge targets are registered nodes or End
        foreach (var (from, edge) in _edges)
        {
            if (!_nodes.ContainsKey(from))
                throw new InvalidOperationException($"Edge source '{from}' is not a registered node.");

            if (edge is UnconditionalEdge u && u.To != End && !_nodes.ContainsKey(u.To))
                throw new InvalidOperationException($"Edge target '{u.To}' is not a registered node.");
        }

        return new AgentGraph(_nodes, _edges, _startNode!);
    }
}
