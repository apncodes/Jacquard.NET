using StrandsAgents.Core;

namespace StrandsAgents.MultiAgent;

// Internal edge types
internal record UnconditionalEdge(string To);
internal record ConditionalEdge(Func<AgentResult, string> Selector);

/// <summary>
/// An executable directed graph of <see cref="IAgent"/> nodes connected by conditional or
/// unconditional edges. Produced by <see cref="GraphBuilder.Build"/>.
/// </summary>
public sealed class AgentGraph
{
    private const int DefaultMaxIterations = 50;

    private readonly Dictionary<string, IAgent> _nodes;
    private readonly Dictionary<string, object> _edges;
    private readonly string _startNode;

    internal AgentGraph(
        Dictionary<string, IAgent> nodes,
        Dictionary<string, object> edges,
        string startNode)
    {
        _nodes = nodes;
        _edges = edges;
        _startNode = startNode;
    }

    /// <summary>
    /// Executes the graph starting from the first registered node, passing
    /// <paramref name="initialPrompt"/> as the initial input.
    /// </summary>
    /// <param name="initialPrompt">The prompt sent to the first node.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="AgentResult"/> produced by the last node to execute.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the iteration cap is exceeded.</exception>
    public async Task<AgentResult> RunAsync(string initialPrompt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initialPrompt);

        var currentNode = _startNode;
        var currentPrompt = initialPrompt;
        AgentResult? lastResult = null;

        for (var iteration = 0; iteration < DefaultMaxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            if (!_nodes.TryGetValue(currentNode, out var agent))
                throw new InvalidOperationException($"Node '{currentNode}' is not registered in the graph.");

            lastResult = await agent.InvokeAsync(currentPrompt, ct).ConfigureAwait(false);

            // Determine next node
            var nextNode = ResolveNextNode(currentNode, lastResult);

            if (nextNode == GraphBuilder.End || nextNode is null)
                return lastResult;

            currentNode = nextNode;
            currentPrompt = lastResult.Message;
        }

        throw new InvalidOperationException(
            $"Graph exceeded the maximum iteration limit of {DefaultMaxIterations}. " +
            "Check for cycles or increase the limit.");
    }

    private string? ResolveNextNode(string currentNode, AgentResult result)
    {
        if (!_edges.TryGetValue(currentNode, out var edge))
            return null; // no outgoing edge → terminate

        return edge switch
        {
            ConditionalEdge conditional => conditional.Selector(result),
            UnconditionalEdge unconditional => unconditional.To,
            _ => null
        };
    }
}
