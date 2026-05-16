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

## Pattern 3: Graph with Conditional Routing

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

## Pattern 4: Agent as Tool

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

## Pattern 5: A2A Protocol

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
| Agent as tool | Orchestrator needs to delegate subtasks dynamically |
| A2A | Agents run in separate processes or are written in different languages |

## Durable multi-step pipelines

For pipelines where individual steps are long-running (minutes) or expensive, use the **Decomposed Sequential Pipeline** pattern with AWS Step Functions. Each agent runs as a separate Lambda function; Step Functions manages checkpointing and retry.

See the [DurableWorkflow sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/DurableWorkflow) for a complete example.
