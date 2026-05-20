---
sidebar_position: 6
---

# Multi-Agent Patterns

## What it is

Multi-agent patterns let you compose multiple specialized agents into a single workflow. Each agent has its own model, tools, and system prompt. The patterns differ in how agents are connected and how data flows between them.

## Problem it solves

A single agent with many tools and a complex system prompt becomes hard to reason about and test. Specialized agents — each focused on one task — are easier to build, test, and improve independently.

## Pattern 1: Sequential Pipeline

Each agent's output becomes the next agent's input. Use when tasks have a natural order and each step depends on the previous.

```csharp
var pipeline = new PipelineOrchestrator([
    researchAgent,
    writerAgent,
    reviewerAgent
]);

var result = await pipeline.InvokeAsync("Write a report on quantum computing");
```

## Pattern 2: Parallel Fan-out

All agents run concurrently on the same input. Use when you need multiple independent perspectives or analyses.

```csharp
var results = await new ParallelOrchestrator([
    techAnalystAgent,
    marketAnalystAgent,
    riskAnalystAgent
]).RunAsync("Analyse this investment opportunity");

// results contains one AgentResult per agent
```

All three run via `Task.WhenAll` — total time is the slowest agent, not the sum.

## Pattern 3: Swarm — dynamic agent-driven handoffs

Unlike the fixed patterns above, a swarm has no predetermined execution path. Each agent receives the full task context — original request, agent history, shared knowledge from previous agents, and the list of available peers — then decides autonomously whether to hand off or terminate.

Routing is implemented by a lightweight internal agent that calls `GetStructuredOutputAsync<SwarmHandoffDecision>` after each node completes. This keeps routing logic separate from the node agents and avoids mutating their tool registries at runtime.

```csharp
var swarm = new SwarmOrchestrator(
[
    new SwarmAgentNode("researcher", researchAgent, "Gathers facts and sources"),
    new SwarmAgentNode("analyst",   analystAgent,  "Structures findings into an outline"),
    new SwarmAgentNode("writer",    writerAgent,   "Drafts the article"),
    new SwarmAgentNode("editor",    editorAgent,   "Reviews and polishes the final article"),
],
routingModel: model,
entryPoint: "researcher",
maxHandoffs: 10,
maxIterations: 12,
executionTimeout: TimeSpan.FromMinutes(10),
nodeTimeout: TimeSpan.FromMinutes(3),
repetitiveHandoffDetectionWindow: 6,   // ping-pong detection
repetitiveHandoffMinUniqueAgents: 3);

// RunAsync — returns SwarmResult when the swarm terminates
var result = await swarm.RunAsync("Write an article about quantum computing");
Console.WriteLine(result.FinalMessage);
Console.WriteLine($"Path: {string.Join(" → ", result.NodeHistory.Select(n => n.AgentId))}");
```

### Observing the swarm in real time

`StreamAsync` yields a typed `SwarmEvent` for every lifecycle moment. Subscribe to it from a console app, an ASP.NET SSE endpoint, a Blazor component, or any `IAsyncEnumerable` consumer.

```csharp
await foreach (var evt in swarm.StreamAsync("Write an article about quantum computing"))
{
    switch (evt)
    {
        case SwarmStartedEvent e:
            Console.WriteLine($"Swarm started → entry: {e.EntryAgentId}");
            break;
        case AgentStartedEvent e:
            Console.WriteLine($"[{e.Iteration}] {e.AgentId} — {e.Description}");
            break;
        case AgentTextDeltaEvent e:
            Console.Write(e.Delta);
            break;
        case AgentToolCallEvent e:
            Console.WriteLine($"  tool: {e.ToolName}");
            break;
        case AgentCompletedEvent e:
            Console.WriteLine($"  tokens: {e.Result.Usage.Total}");
            break;
        case HandoffEvent e:
            Console.WriteLine($"  → handoff: {e.FromAgentId} → {e.ToAgentId}");
            break;
        case SwarmCompletedEvent e:
            Console.WriteLine($"Done. Status: {e.Status}, Total tokens: {e.TotalUsage.Total}");
            break;
    }
}
```

### SwarmEvent hierarchy

| Event | When it fires |
|---|---|
| `SwarmStartedEvent` | Once at the start — task + entry agent ID |
| `AgentStartedEvent` | Before each agent runs — ID, description, handoff message, iteration |
| `AgentTextDeltaEvent` | Each streaming token from the active agent |
| `AgentToolCallEvent` | When an agent invokes a tool |
| `AgentToolResultEvent` | When a tool call returns |
| `AgentCompletedEvent` | When an agent finishes — full `AgentResult` with token usage |
| `HandoffEvent` | When routing decides to transfer — from/to agent IDs + message |
| `SwarmCompletedEvent` | Always the last event — status, final message, history, total tokens |

### Safety bounds

| Parameter | Default | Purpose |
|---|---|---|
| `maxHandoffs` | 20 | Maximum agent-to-agent handoffs |
| `maxIterations` | 20 | Maximum total agent invocations |
| `executionTimeout` | 15 min | Wall-clock ceiling for the entire swarm |
| `nodeTimeout` | 5 min | Per-agent wall-clock ceiling |
| `repetitiveHandoffDetectionWindow` | 0 (off) | Sliding window for ping-pong detection |
| `repetitiveHandoffMinUniqueAgents` | 0 | Minimum unique agents in the window |

## Pattern 4: Graph with Conditional Routing

Agents are nodes in a directed graph. Edges can be conditional — the next node is chosen based on the previous agent's output. Use for triage, classification, and workflows with branching logic.

```csharp
var graph = new GraphBuilder()
    .AddNode("triage",    triageAgent)
    .AddNode("billing",   billingAgent)
    .AddNode("technical", techAgent)
    .AddConditionalEdge("triage", result =>
        result.Message.Contains("billing") ? "billing" : "technical")
    .Build();

var result = await graph.InvokeAsync("I was charged twice for my subscription");
```

## Pattern 5: Agent as Tool

Wrap any agent as a tool that another agent can call. Use for hierarchical orchestration — an orchestrator agent delegates subtasks to specialist agents.

```csharp
var researchTool = researchAgent.AsTool(
    name: "researcher",
    description: "Research a topic and return a detailed summary");

var writerAgent = new Agent(
    model: model,
    systemPrompt: "You are a writer. Use the researcher tool to gather information.",
    tools: [researchTool]);
```

## Pattern 6: A2A Protocol

Call agents running in separate processes or on separate machines using the Agent-to-Agent (A2A) protocol. Works across languages and frameworks.

**Server side** (expose an agent over HTTP):

```csharp
app.MapA2AEndpoint("/agent", agent);
```

**Client side** (call a remote agent):

```csharp
using var remote = new A2AAgent(new Uri("http://python-service/agent"));
var result = await remote.InvokeAsync("Research this topic");
```

## Choosing a pattern

| Pattern | Use when |
|---|---|
| Pipeline | Tasks have a natural sequence, each step depends on the previous |
| Parallel | Multiple independent analyses needed simultaneously |
| Graph | Workflow has conditional branching or routing logic |
| Swarm | Agents need to collaborate autonomously — no fixed path, dynamic handoffs |
| Agent as tool | Orchestrator needs to delegate subtasks dynamically |
| A2A | Agents run in separate processes or are written in different languages |

## Durable multi-step pipelines

For pipelines where individual steps are long-running (minutes) or expensive, use the **Decomposed Sequential Pipeline** pattern with AWS Step Functions. Each agent runs as a separate Lambda function; Step Functions manages checkpointing and retry.

See the [DurableWorkflow sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/DurableWorkflow) for a complete example.
