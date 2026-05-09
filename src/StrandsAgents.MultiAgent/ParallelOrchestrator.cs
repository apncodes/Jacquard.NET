using StrandsAgents.Core;

namespace StrandsAgents.MultiAgent;

/// <summary>
/// Invokes multiple agents in parallel with the same prompt and collects all results.
/// </summary>
public sealed class ParallelOrchestrator
{
    private readonly IReadOnlyList<IAgent> _agents;

    /// <summary>
    /// Initializes a new <see cref="ParallelOrchestrator"/>.
    /// </summary>
    /// <param name="agents">Agents to invoke in parallel.</param>
    public ParallelOrchestrator(IEnumerable<IAgent> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);
        _agents = agents.ToList().AsReadOnly();
        if (_agents.Count == 0)
            throw new ArgumentException("Orchestrator must contain at least one agent.", nameof(agents));
    }

    /// <summary>
    /// Invokes all agents with <paramref name="prompt"/> in parallel using
    /// <see cref="Task.WhenAll"/>, and returns all results in agent-registration order.
    /// </summary>
    /// <param name="prompt">The prompt sent to every agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Results in the same order as the agents were registered.</returns>
    public async Task<IReadOnlyList<AgentResult>> RunAsync(
        string prompt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var tasks = _agents.Select(a => a.InvokeAsync(prompt, ct));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
