# SwarmResearch — Equity Research Swarm (Console)

Demonstrates the **Swarm** multi-agent pattern using `SwarmOrchestrator` from `StrandsAgents.MultiAgent`. Four specialist agents collaborate autonomously via dynamic handoffs to produce a technology article.

## Architecture

```
researcher → analyst → writer → editor
```

Unlike `ParallelOrchestrator` (fixed fan-out/fan-in) or `PipelineOrchestrator` (fixed sequence), the swarm has **no predetermined execution path**. Each agent decides whether to hand off to a peer or terminate. The routing decision is extracted via `GetStructuredOutputAsync<SwarmHandoffDecision>` after each agent completes — no tool injection, no runtime mutation of agent registries.

| Agent | Tools | Role |
|---|---|---|
| `researcher` | `SearchFacts`, `GetSources` | Gathers verified facts and authoritative sources |
| `analyst` | — | Organises research into a structured outline |
| `writer` | — | Writes the article draft |
| `editor` | `ReviewDraft` | Reviews, applies critique, produces final article |

## SDK features shown

- `SwarmOrchestrator.StreamAsync` — `IAsyncEnumerable<SwarmEvent>` stream of typed lifecycle events
- `SwarmEvent` hierarchy — `SwarmStartedEvent`, `AgentStartedEvent`, `AgentTextDeltaEvent`, `AgentToolCallEvent`, `AgentToolResultEvent`, `AgentCompletedEvent`, `HandoffEvent`, `SwarmCompletedEvent`
- `SwarmAgentNode` — named agent with optional description used for routing hints
- `[Tool]` source generator — compile-time `ITool` wrappers, zero runtime reflection

## Prerequisites

AWS credentials configured (env vars, `~/.aws/credentials`, or IAM role) with Amazon Bedrock access.

## Usage

```bash
dotnet run --project samples/SwarmResearch                    # "large language models"
dotnet run --project samples/SwarmResearch -- "quantum computing"
dotnet run --project samples/SwarmResearch -- "renewable energy"
```

## Console output

The sample subscribes to `StreamAsync` and renders each event type with colour-coded output:

- Agent start/complete with iteration number and token counts
- Tool calls with running/done status
- Handoff banners showing from/to agent and message preview
- Final summary: execution path, total tokens, elapsed time

## Swarm safety bounds

```csharp
var swarm = new SwarmOrchestrator(nodes, routingModel: model,
    maxHandoffs: 10,
    maxIterations: 12,
    executionTimeout: TimeSpan.FromMinutes(10),
    nodeTimeout: TimeSpan.FromMinutes(3),
    repetitiveHandoffDetectionWindow: 6,
    repetitiveHandoffMinUniqueAgents: 3);
```

`repetitiveHandoffDetectionWindow` and `repetitiveHandoffMinUniqueAgents` detect ping-pong behaviour — if the last 6 handoffs involve fewer than 3 unique agents, the swarm terminates.

## Subscribing to swarm events in your own application

```csharp
await foreach (var evt in swarm.StreamAsync(task))
{
    switch (evt)
    {
        case AgentStartedEvent e:
            Console.WriteLine($"[{e.Iteration}] {e.AgentId} starting...");
            break;
        case AgentTextDeltaEvent e:
            Console.Write(e.Delta);
            break;
        case HandoffEvent e:
            Console.WriteLine($"Handoff: {e.FromAgentId} → {e.ToAgentId}");
            break;
        case SwarmCompletedEvent e:
            Console.WriteLine($"Done. Status: {e.Status}, Tokens: {e.TotalUsage.Total}");
            break;
    }
}
```

Use `RunAsync` if you only need the final result without streaming:

```csharp
var result = await swarm.RunAsync(task);
Console.WriteLine(result.FinalMessage);
```
