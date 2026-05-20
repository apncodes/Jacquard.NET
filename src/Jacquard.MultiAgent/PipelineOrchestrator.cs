using Jacquard.Core;
using System.Runtime.CompilerServices;

namespace Jacquard.MultiAgent;

/// <summary>
/// Runs a sequence of agents in order, passing each agent's final text response as the
/// input prompt to the next stage.
/// </summary>
public sealed class PipelineOrchestrator
{
    private readonly IReadOnlyList<(IAgent Agent, string? Name)> _stages;

    /// <summary>
    /// Initializes a new <see cref="PipelineOrchestrator"/> with the given stages.
    /// </summary>
    /// <param name="stages">Agents to execute in order.</param>
    public PipelineOrchestrator(IEnumerable<IAgent> stages)
        : this(stages?.Select(a => (a, (string?)null)) ?? throw new ArgumentNullException(nameof(stages)))
    {
    }

    /// <summary>
    /// Initializes a new <see cref="PipelineOrchestrator"/> with named stages.
    /// </summary>
    /// <param name="stages">Agents paired with optional names, executed in order.</param>
    public PipelineOrchestrator(IEnumerable<(IAgent Agent, string? Name)> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        _stages = stages.ToList().AsReadOnly();
        if (_stages.Count == 0)
            throw new ArgumentException("Pipeline must contain at least one stage.", nameof(stages));
    }

    /// <summary>
    /// Runs all pipeline stages sequentially and returns the final stage's result.
    /// Each stage receives the previous stage's <see cref="AgentResult.Message"/> as its prompt.
    /// </summary>
    /// <param name="initialPrompt">The input prompt for the first stage.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="AgentResult"/> from the final stage.</returns>
    public async Task<AgentResult> RunAsync(string initialPrompt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initialPrompt);

        var prompt = initialPrompt;
        AgentResult result = default!;

        foreach (var (agent, _) in _stages)
        {
            ct.ThrowIfCancellationRequested();
            result = await agent.InvokeAsync(prompt, ct).ConfigureAwait(false);
            prompt = result.Message;
        }

        return result;
    }

    /// <summary>
    /// Runs all pipeline stages sequentially and yields <see cref="PipelineStageEvent"/>
    /// wrappers around each stage's streaming events.
    /// </summary>
    /// <param name="initialPrompt">The input prompt for the first stage.</param>
    /// <param name="ct">Cancellation token.</param>
    public async IAsyncEnumerable<PipelineStageEvent> StreamAsync(
        string initialPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(initialPrompt);

        var prompt = initialPrompt;

        for (var i = 0; i < _stages.Count; i++)
        {
            var (agent, name) = _stages[i];
            string? stageOutput = null;

            await foreach (var evt in agent.StreamAsync(prompt, ct).ConfigureAwait(false))
            {
                if (evt is AgentCompleteEvent complete)
                    stageOutput = complete.Result.Message;

                yield return new PipelineStageEvent(i, name, evt);
            }

            prompt = stageOutput ?? string.Empty;
        }
    }
}
