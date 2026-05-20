using Jacquard.Core;
using System.Runtime.CompilerServices;
using System.Text;

namespace Jacquard.MultiAgent;

/// <summary>
/// Runs a dynamic, agent-driven handoff chain where each agent decides whether to
/// continue working or transfer control to a peer with different expertise.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="ParallelOrchestrator"/> (fan-out/fan-in) or
/// <see cref="PipelineOrchestrator"/> (fixed sequence), a swarm has no predetermined
/// execution path. Each agent receives the full task context — original request, agent
/// history, shared knowledge contributed by previous agents, and the list of available
/// peers — then decides autonomously whether to hand off or terminate.
/// </para>
/// <para>
/// Use <see cref="RunAsync"/> for a simple fire-and-forget invocation that returns a
/// <see cref="SwarmResult"/> when the swarm completes.
/// </para>
/// <para>
/// Use <see cref="StreamAsync"/> to observe the swarm in real time. It yields a rich
/// stream of <see cref="SwarmEvent"/> records covering every lifecycle moment: swarm
/// start, agent start/complete, streaming text tokens, tool calls, handoffs, and the
/// final <see cref="SwarmCompletedEvent"/>. <see cref="RunAsync"/> is implemented by
/// consuming <see cref="StreamAsync"/> internally, so both paths share identical logic.
/// </para>
/// <para>
/// Routing is implemented by a lightweight internal <see cref="Agent"/> that calls
/// <c>GetStructuredOutputAsync&lt;SwarmHandoffDecision&gt;</c> after each node completes.
/// This keeps routing logic separate from the node agents, avoids mutating their tool
/// registries at runtime, and keeps individual agents reusable outside the swarm.
/// </para>
/// <para>
/// Safety bounds: <see cref="MaxHandoffs"/>, <see cref="MaxIterations"/>,
/// <see cref="ExecutionTimeout"/>, and <see cref="NodeTimeout"/> prevent runaway execution.
/// Ping-pong detection is available via <see cref="RepetitiveHandoffDetectionWindow"/> and
/// <see cref="RepetitiveHandoffMinUniqueAgents"/>.
/// </para>
/// </remarks>
public sealed class SwarmOrchestrator
{
    private readonly IReadOnlyDictionary<string, SwarmAgentNode> _nodes;
    private readonly string _entryPoint;
    private readonly IModel _routingModel;

    /// <summary>Maximum number of agent-to-agent handoffs before the swarm is force-terminated.</summary>
    public int MaxHandoffs { get; }

    /// <summary>Maximum total agent invocations (including the entry point) before force-termination.</summary>
    public int MaxIterations { get; }

    /// <summary>Wall-clock ceiling for the entire swarm execution.</summary>
    public TimeSpan ExecutionTimeout { get; }

    /// <summary>Per-node wall-clock ceiling. Applied to every agent invocation.</summary>
    public TimeSpan NodeTimeout { get; }

    /// <summary>
    /// Number of recent handoffs to inspect for ping-pong behaviour.
    /// Set to 0 (default) to disable detection.
    /// </summary>
    public int RepetitiveHandoffDetectionWindow { get; }

    /// <summary>
    /// Minimum number of unique agents required within the last
    /// <see cref="RepetitiveHandoffDetectionWindow"/> handoffs.
    /// Ignored when <see cref="RepetitiveHandoffDetectionWindow"/> is 0.
    /// </summary>
    public int RepetitiveHandoffMinUniqueAgents { get; }

    /// <summary>
    /// Initializes a new <see cref="SwarmOrchestrator"/>.
    /// </summary>
    /// <param name="nodes">
    /// The agent nodes that participate in the swarm. Must contain at least one node.
    /// </param>
    /// <param name="routingModel">
    /// The model used by the internal routing agent to extract handoff decisions.
    /// Typically the same model used by the swarm agents.
    /// </param>
    /// <param name="entryPoint">
    /// ID of the agent that receives the initial prompt.
    /// Defaults to the first node in <paramref name="nodes"/>.
    /// </param>
    /// <param name="maxHandoffs">Maximum agent-to-agent handoffs. Default 20.</param>
    /// <param name="maxIterations">Maximum total agent invocations. Default 20.</param>
    /// <param name="executionTimeout">Overall timeout. Default 15 minutes.</param>
    /// <param name="nodeTimeout">Per-agent timeout. Default 5 minutes.</param>
    /// <param name="repetitiveHandoffDetectionWindow">
    /// Sliding window size for ping-pong detection. Default 0 (disabled).
    /// </param>
    /// <param name="repetitiveHandoffMinUniqueAgents">
    /// Minimum unique agents required in the detection window. Default 0.
    /// </param>
    public SwarmOrchestrator(
        IEnumerable<SwarmAgentNode> nodes,
        IModel routingModel,
        string? entryPoint = null,
        int maxHandoffs = 20,
        int maxIterations = 20,
        TimeSpan? executionTimeout = null,
        TimeSpan? nodeTimeout = null,
        int repetitiveHandoffDetectionWindow = 0,
        int repetitiveHandoffMinUniqueAgents = 0)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(routingModel);

        var nodeList = nodes.ToList();
        if (nodeList.Count == 0)
            throw new ArgumentException("Swarm must contain at least one agent node.", nameof(nodes));

        _nodes = nodeList.ToDictionary(n => n.Id, StringComparer.Ordinal);
        _routingModel = routingModel;
        _entryPoint = entryPoint ?? nodeList[0].Id;

        if (!_nodes.ContainsKey(_entryPoint))
            throw new ArgumentException(
                $"Entry point '{_entryPoint}' is not registered in the swarm.", nameof(entryPoint));

        MaxHandoffs = maxHandoffs;
        MaxIterations = maxIterations;
        ExecutionTimeout = executionTimeout ?? TimeSpan.FromMinutes(15);
        NodeTimeout = nodeTimeout ?? TimeSpan.FromMinutes(5);
        RepetitiveHandoffDetectionWindow = repetitiveHandoffDetectionWindow;
        RepetitiveHandoffMinUniqueAgents = repetitiveHandoffMinUniqueAgents;
    }

    /// <summary>
    /// Executes the swarm and returns the final result when complete.
    /// Internally consumes <see cref="StreamAsync"/> — all logic is shared.
    /// </summary>
    /// <param name="task">The initial task sent to the entry-point agent.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<SwarmResult> RunAsync(string task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        SwarmCompletedEvent? completed = null;

        await foreach (var evt in StreamAsync(task, ct).ConfigureAwait(false))
        {
            if (evt is SwarmCompletedEvent c)
                completed = c;
        }

        // StreamAsync always emits SwarmCompletedEvent as its last item
        return new SwarmResult(
            completed!.FinalMessage,
            completed.NodeHistory,
            completed.Status,
            completed.TotalUsage);
    }

    /// <summary>
    /// Executes the swarm and streams <see cref="SwarmEvent"/> records in real time.
    /// </summary>
    /// <remarks>
    /// The event sequence for a typical two-agent handoff looks like:
    /// <list type="number">
    ///   <item><see cref="SwarmStartedEvent"/></item>
    ///   <item><see cref="AgentStartedEvent"/> (agent A)</item>
    ///   <item><see cref="AgentToolCallEvent"/> (if agent A calls a tool)</item>
    ///   <item><see cref="AgentToolResultEvent"/></item>
    ///   <item><see cref="AgentTextDeltaEvent"/> × N (streaming tokens)</item>
    ///   <item><see cref="AgentCompletedEvent"/> (agent A)</item>
    ///   <item><see cref="HandoffEvent"/> (A → B)</item>
    ///   <item><see cref="AgentStartedEvent"/> (agent B)</item>
    ///   <item>… agent B events …</item>
    ///   <item><see cref="AgentCompletedEvent"/> (agent B)</item>
    ///   <item><see cref="SwarmCompletedEvent"/></item>
    /// </list>
    /// The final event is always <see cref="SwarmCompletedEvent"/>.
    /// </remarks>
    /// <param name="task">The initial task sent to the entry-point agent.</param>
    /// <param name="ct">Cancellation token.</param>
    public async IAsyncEnumerable<SwarmEvent> StreamAsync(
        string task,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        // Lightweight routing agent — stateless, no tools, no conversation history.
        var router = new Agent(_routingModel);

        using var timeoutCts = new CancellationTokenSource(ExecutionTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var nodeHistory = new List<SwarmNodeResult>();
        var sharedKnowledge = new Dictionary<string, string>(StringComparer.Ordinal);
        var recentAgentIds = new Queue<string>();
        var currentAgentId = _entryPoint;
        string? handoffMessage = null;
        var handoffs = 0;
        var iterations = 0;
        var totalUsage = TokenUsage.Zero;

        yield return new SwarmStartedEvent(task, _entryPoint);

        while (iterations < MaxIterations && handoffs <= MaxHandoffs)
        {
            linkedCts.Token.ThrowIfCancellationRequested();

            if (!_nodes.TryGetValue(currentAgentId, out var node))
                throw new InvalidOperationException(
                    $"Agent '{currentAgentId}' is not registered in the swarm.");

            var prompt = BuildPrompt(task, handoffMessage, nodeHistory, sharedKnowledge, currentAgentId);

            yield return new AgentStartedEvent(
                currentAgentId,
                node.Description,
                handoffMessage,
                iterations + 1);

            // Stream the agent — collect translated events into a buffer first because
            // C# does not allow yield inside a try/catch block.
            var eventBuffer = new List<SwarmEvent>();
            AgentResult? agentResult = null;
            bool timedOut = false;

            try
            {
                using var nodeCts = new CancellationTokenSource(NodeTimeout);
                using var nodeLinked = CancellationTokenSource.CreateLinkedTokenSource(
                    linkedCts.Token, nodeCts.Token);

                await foreach (var innerEvt in node.Agent.StreamAsync(prompt, nodeLinked.Token)
                    .ConfigureAwait(false))
                {
                    switch (innerEvt)
                    {
                        case TextDeltaEvent td:
                            eventBuffer.Add(new AgentTextDeltaEvent(currentAgentId, td.Delta));
                            break;
                        case ToolCallStartEvent tc:
                            eventBuffer.Add(new AgentToolCallEvent(currentAgentId, tc.ToolName));
                            break;
                        case ToolCallResultEvent tr:
                            eventBuffer.Add(new AgentToolResultEvent(currentAgentId, tr.ToolCallId, tr.Result));
                            break;
                        case AgentCompleteEvent ac:
                            agentResult = ac.Result;
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                timedOut = true;
            }

            // Yield buffered events outside the try/catch
            foreach (var buffered in eventBuffer)
                yield return buffered;

            if (timedOut || agentResult is null)
            {
                yield return new SwarmCompletedEvent(
                    SwarmStatus.TimedOut,
                    nodeHistory.Count > 0 ? nodeHistory[^1].Message : string.Empty,
                    nodeHistory,
                    totalUsage);
                yield break;
            }

            nodeHistory.Add(new SwarmNodeResult(currentAgentId, agentResult.Message, agentResult.Usage));
            totalUsage += agentResult.Usage;
            iterations++;

            yield return new AgentCompletedEvent(currentAgentId, agentResult);

            // Ping-pong tracking
            if (RepetitiveHandoffDetectionWindow > 0)
            {
                recentAgentIds.Enqueue(currentAgentId);
                if (recentAgentIds.Count > RepetitiveHandoffDetectionWindow)
                    recentAgentIds.Dequeue();
            }

            // Routing decision
            SwarmHandoffDecision? decision = null;
            bool routingTimedOut = false;

            try
            {
                using var nodeCts = new CancellationTokenSource(NodeTimeout);
                using var nodeLinked = CancellationTokenSource.CreateLinkedTokenSource(
                    linkedCts.Token, nodeCts.Token);

                decision = await router.GetStructuredOutputAsync<SwarmHandoffDecision>(
                    BuildHandoffPrompt(agentResult.Message, currentAgentId),
                    nodeLinked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                routingTimedOut = true;
            }

            if (routingTimedOut || decision is null)
            {
                yield return new SwarmCompletedEvent(
                    SwarmStatus.TimedOut,
                    nodeHistory[^1].Message,
                    nodeHistory,
                    totalUsage);
                yield break;
            }

            // Accumulate shared knowledge
            if (decision.Context is not null)
                foreach (var (k, v) in decision.Context)
                    sharedKnowledge[$"{currentAgentId}.{k}"] = v;

            // Terminate?
            if (decision.NextAgentId is null || !_nodes.ContainsKey(decision.NextAgentId))
            {
                yield return new SwarmCompletedEvent(
                    SwarmStatus.Completed,
                    decision.Message,
                    nodeHistory,
                    totalUsage);
                yield break;
            }

            // Ping-pong check
            if (RepetitiveHandoffDetectionWindow > 0 &&
                recentAgentIds.Count >= RepetitiveHandoffDetectionWindow)
            {
                var uniqueCount = recentAgentIds.Distinct(StringComparer.Ordinal).Count();
                if (uniqueCount < RepetitiveHandoffMinUniqueAgents)
                {
                    yield return new SwarmCompletedEvent(
                        SwarmStatus.Completed,
                        nodeHistory[^1].Message,
                        nodeHistory,
                        totalUsage);
                    yield break;
                }
            }

            yield return new HandoffEvent(currentAgentId, decision.NextAgentId, decision.Message);

            handoffMessage = decision.Message;
            currentAgentId = decision.NextAgentId;
            handoffs++;
        }

        // Safety-bound termination
        var status = iterations >= MaxIterations
            ? SwarmStatus.MaxIterationsReached
            : SwarmStatus.MaxHandoffsReached;

        yield return new SwarmCompletedEvent(
            status,
            nodeHistory.Count > 0 ? nodeHistory[^1].Message : string.Empty,
            nodeHistory,
            totalUsage);
    }

    // ── private helpers ──────────────────────────────────────────────────────────

    private string BuildPrompt(
        string task,
        string? handoffMessage,
        List<SwarmNodeResult> history,
        Dictionary<string, string> knowledge,
        string currentAgentId)
    {
        var sb = new StringBuilder();

        if (handoffMessage is not null)
        {
            sb.AppendLine($"Handoff Message: {handoffMessage}");
            sb.AppendLine();
        }

        sb.AppendLine($"User Request: {task}");
        sb.AppendLine();

        if (history.Count > 0)
        {
            sb.AppendLine($"Previous agents who worked on this: {string.Join(" → ", history.Select(h => h.AgentId))}");
            sb.AppendLine();
        }

        if (knowledge.Count > 0)
        {
            sb.AppendLine("Shared knowledge from previous agents:");
            foreach (var (k, v) in knowledge)
                sb.AppendLine($"  • {k}: {v}");
            sb.AppendLine();
        }

        var available = GetAvailableAgentDescriptions(currentAgentId);
        if (!string.IsNullOrEmpty(available))
        {
            sb.AppendLine("Other agents available for collaboration:");
            sb.AppendLine(available);
        }

        return sb.ToString();
    }

    private string BuildHandoffPrompt(string agentResponse, string currentAgentId)
    {
        var available = GetAvailableAgentIds(currentAgentId);
        return $"""
            An agent named '{currentAgentId}' just produced the following response.
            Based on that response, decide the next step.

            Available agent IDs to hand off to: {available}
            (Set NextAgentId to null to terminate the swarm and treat Message as the final answer.)

            Respond with a JSON object:
            - NextAgentId: string or null
            - Message: instructions for the next agent, or the final answer if terminating
            - Context: optional key-value pairs of knowledge to share with future agents (can be null)

            Agent response:
            {agentResponse}
            """;
    }

    private string GetAvailableAgentIds(string excludeId) =>
        string.Join(", ", _nodes.Keys.Where(id => id != excludeId));

    private string GetAvailableAgentDescriptions(string excludeId)
    {
        var sb = new StringBuilder();
        foreach (var (id, node) in _nodes)
        {
            if (id == excludeId) continue;
            sb.Append($"  Agent name: {id}");
            if (!string.IsNullOrWhiteSpace(node.Description))
                sb.Append($". Agent description: {node.Description}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
