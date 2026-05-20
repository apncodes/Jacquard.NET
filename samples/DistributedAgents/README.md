# DistributedAgents

Two separate processes: a `ResearchService` that exposes an agent over HTTP, and a `WriterClient` that treats it as a callable tool. The writer agent never knows or cares that the researcher runs in a different process — it just calls a tool named `researcher`.

## SDK concepts demonstrated

**`MapA2AEndpoint`** — one line in `ResearchService/Program.cs` exposes any `IAgent` as a POST endpoint that speaks the A2A (Agent-to-Agent) protocol: `{ "prompt": "..." }` in, `{ "message": "...", "stopReason": "...", "inputTokens": 0, "outputTokens": 0 }` out.

**`A2AAgent`** — a client-side `IAgent` implementation that forwards `InvokeAsync` calls over HTTP to any A2A endpoint. From the writer's perspective it is indistinguishable from a local agent. Implements `IDisposable` and owns its `HttpClient` when no external client is passed.

**`AgentTool`** — wraps any `IAgent` as an `ITool` so a parent agent can call it by name in a tool-use turn. The writer agent calls `researcher(prompt: "...")` and receives the research brief as a tool result, then writes the article.

## Scenario

The `WriterClient` is given a topic. Its writer agent decides it needs background research and calls the `researcher` tool. That tool call crosses the network to `ResearchService`, which runs a Bedrock agent with a web search tool and returns a research brief. The writer then composes a three-paragraph article from that brief.

## How to run

**Terminal 1 — start the service first:**

```bash
dotnet run --project samples/DistributedAgents/ResearchService
# Listening on http://localhost:5100
```

**Terminal 2 — run the client:**

```bash
dotnet run --project samples/DistributedAgents/WriterClient
dotnet run --project samples/DistributedAgents/WriterClient -- "quantum computing"
dotnet run --project samples/DistributedAgents/WriterClient -- "climate change"
```

## Where you'd use these patterns

- **Microservice AI architectures** — specialist agents deployed and scaled independently; any service can call any other via A2A without shared libraries or in-process coupling.
- **Cross-language interop** — A2A is the same protocol used by the Python Jacquard SDK. A Python research agent can serve as a tool for a C# writer agent, or vice versa.
- **Reusable agent APIs** — expose a single well-tested agent (legal review, code analysis, data extraction) as an HTTP service and let any number of orchestrators call it.
