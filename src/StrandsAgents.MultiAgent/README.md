# StrandsAgents.MultiAgent

Multi-agent orchestration for [Strands Agents .NET](https://github.com/apncodes/StrandsAgents.net).

```bash
dotnet add package StrandsAgents.MultiAgent
```

```csharp
using StrandsAgents.MultiAgent;

// Sequential pipeline — each stage receives the previous output as its prompt
var pipeline = new PipelineOrchestrator([researchAgent, writerAgent, reviewerAgent]);
var result = await pipeline.InvokeAsync("Write a report on quantum computing");

// Parallel fan-out — Task.WhenAll over all agents
var results = await new ParallelOrchestrator([agent1, agent2, agent3])
    .RunAsync("Analyse this from your specialist perspective");

// Swarm — dynamic agent-driven handoff chain with real-time event streaming
var swarm = new SwarmOrchestrator(
[
    new SwarmAgentNode("researcher", researchAgent, "Gathers facts and sources"),
    new SwarmAgentNode("writer",     writerAgent,   "Drafts the article"),
    new SwarmAgentNode("editor",     editorAgent,   "Reviews and polishes"),
],
routingModel: model,
entryPoint: "researcher");

// RunAsync — returns SwarmResult when complete
var result = await swarm.RunAsync("Write an article about quantum computing");

// StreamAsync — IAsyncEnumerable<SwarmEvent> for real-time observation
await foreach (var evt in swarm.StreamAsync("Write an article about quantum computing"))
{
    switch (evt)
    {
        case AgentStartedEvent e:   Console.WriteLine($"[{e.Iteration}] {e.AgentId}"); break;
        case AgentTextDeltaEvent e: Console.Write(e.Delta); break;
        case HandoffEvent e:        Console.WriteLine($"→ {e.ToAgentId}"); break;
        case SwarmCompletedEvent e: Console.WriteLine($"Done: {e.Status}"); break;
    }
}

// Graph routing — LLM decides the next node
var graph = new GraphBuilder()
    .AddNode("triage", triageAgent)
    .AddNode("billing", billingAgent)
    .AddNode("technical", techAgent)
    .AddConditionalEdge("triage", r => r.Message.Contains("billing") ? "billing" : "technical")
    .Build();

// A2A — call a remote agent over HTTP (cross-framework, cross-language)
using var remote = new A2AAgent(new Uri("http://python-service/agent"));
var result = await remote.InvokeAsync("Research this topic");
```
