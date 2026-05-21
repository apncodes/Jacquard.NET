# Jacquard.NET

> **Native C# SDK for building agentic AI.** Inspired by the [Strands Agents](https://strandsagents.com) design principles. Community maintained.

[![NuGet](https://img.shields.io/nuget/v/Jacquard.Core?label=NuGet&color=blue)](https://www.nuget.org/packages/Jacquard.Core)
[![CI](https://github.com/apncodes/Jacquard.NET/actions/workflows/ci.yml/badge.svg)](https://github.com/apncodes/Jacquard.NET/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-orange)](https://apncodes.github.io/Jacquard.NET/)
[![License](https://img.shields.io/badge/license-Apache--2.0-green)](https://github.com/apncodes/Jacquard.NET/blob/main/LICENSE)

---

## Why Jacquard.NET

The Jacquard loom — invented in 1804 — was the first machine to use punch cards to control patterns. It inspired Charles Babbage. It is the origin of the idea that a machine could be programmed to weave any pattern from simple instructions. That is what agents do: weave tools, models, loops, and prompts into intelligent behavior.

The .NET ecosystem is the dominant runtime in enterprise — Lambda functions, Windows services, ASP.NET APIs. When AWS released Strands Agents, the design was immediately compelling: model-driven event loop, clean tool system, hooks, multi-agent orchestration. But there was no native .NET implementation.

Jacquard.NET is that implementation. Built ground-up in C# 13. The same design principles as Strands Agents, expressed in the patterns .NET developers already know.

**Four principles guide every decision:**

1. **Don't over-engineer** — if it doesn't need to be a feature, it isn't one
2. **Keep things clean** — idiomatic C# throughout, no proprietary abstractions
3. **Embrace open standards** — MCP and A2A native, not bolted on
4. **Be pragmatic about what to ship** — production-useful, not academically complete

**Five load-bearing technical pillars:**

1. **Easy to learn, idiomatic to write** — if you can write a C# method, you can write a tool. No new programming model, no middleware pipelines to learn before first invocation.
2. **Industry-standard vocabulary** — agent, tool, system prompt, delta, session, hook. Reads natively to anyone from Strands Python, OpenAI, Anthropic, or LangChain.
3. **Zero runtime reflection** — compile-time tool dispatch via Roslyn source generators. `JACQUARD001` diagnostic catches misconfiguration at build time.
4. **NativeAOT-ready** — measured 89.6ms average cold-start init on AWS Lambda (arm64 Graviton2, 19/20 runs under 100ms). Reflection-free hot path designed for AOT publish.
5. **Multi-agent in one package** — pipeline, parallel, graph orchestration, agent-as-tool, A2A protocol for cross-language interop.

---

## Quickstart

```
dotnet add package Jacquard.Core
dotnet add package Jacquard.Models.Bedrock
dotnet add package Jacquard.Tools
dotnet add package Jacquard.SourceGenerator
```

Decorate a method with `[Tool]` on a `partial class` — the Roslyn source generator emits a compile-time `ITool` wrapper and an `IToolProvider` implementation automatically.

**Single-file option** — put the class declaration after the top-level statements:

```csharp
using Jacquard.Core;
using Jacquard.Models.Bedrock;
using MyApp;

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]
);

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);

// Type declarations must come after top-level statements in the same file.
// Use a block-body namespace (not file-scoped) when mixing with top-level statements.
namespace MyApp
{
    public partial class WeatherTools
    {
        [Tool("Returns the current weather for a city")]
        public string GetWeather(string city) => $"Sunny, 22°C in {city}";
    }
}
```

**Two-file option** — cleaner for larger projects:

**WeatherTools.cs**

```csharp
using Jacquard.Core;

namespace MyApp;

public partial class WeatherTools
{
    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";
}
```

**Program.cs**

```csharp
using Jacquard.Core;
using Jacquard.Models.Bedrock;
using MyApp;

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]
);

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);
```

> The namespace on the `partial class` is required. The source generator emits its `IToolProvider` implementation in the same namespace, and C# merges the two partial declarations into one type. Without a matching namespace they are treated as separate types and the build fails.
>
> Prerequisites: .NET 10 SDK, AWS credentials with Bedrock access enabled.

---

## Model providers

Four providers are included out of the box — swap in one line:

```csharp
// Amazon Bedrock (cross-region inference profile)
var model = new BedrockModel(region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// Anthropic direct API
var model = new AnthropicModel(apiKey: "sk-ant-...", modelId: "claude-sonnet-4-5");

// OpenAI / Azure OpenAI / Ollama / any OpenAI-compatible endpoint
var model = new OpenAICompatibleModel(
    baseUrl: "https://api.openai.com/v1",
    apiKey: "sk-...",
    modelId: "gpt-4o");

// Google Gemini
var model = new GeminiModel(apiKey: "...", modelId: "gemini-2.5-flash");
```

---

## Streaming

```csharp
await foreach (var evt in agent.StreamAsync("Explain async/await in C#"))
{
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Delta);
}
```

---

## Structured output

```csharp
record WeatherReport(string City, int TempC, string Condition);

var report = await agent.GetStructuredOutputAsync<WeatherReport>(
    "What is the weather in Paris right now?");

Console.WriteLine($"{report.City}: {report.TempC}°C, {report.Condition}");
```

---

## DI integration (ASP.NET Core / Worker Service)

```
dotnet add package Jacquard.Extensions.DI
```

```csharp
builder.Services
    .AddBedrockModel(region: "us-east-1")
    .AddHttpRequestTool()
    .AddJacquardToolProvider<WeatherTools>()
    .AddJacquardInMemorySessionManager()
    .AddJacquardAgent();

// Resolve IAgent from the container
var agent = app.Services.GetRequiredService<IAgent>();
```

---

## Features

- **Model-driven event loop** — the LLM decides which tools to call; the SDK executes them and loops until `EndTurn`
- **Tool system** — decorate any `partial` class method with `[Tool]`; the Roslyn source generator emits a compile-time `ITool` wrapper with zero runtime reflection
- **`IToolProvider` pattern** — pass your tool class directly to `Agent` via `toolProviders:`; no generated wrapper type names in user code; `JACQUARD001` warning guides non-partial classes
- **Streaming** — `StreamAsync` returns `IAsyncEnumerable<StreamEvent>` end to end with `[EnumeratorCancellation]` on every boundary
- **Hook system** — type-safe `Register<TEvent>` callbacks for `BeforeToolCall`, `AfterToolCall`, `BeforeModelCall`, `AfterModelCall`
- **Human-in-the-loop** — set `e.Interrupt = true` in any `BeforeToolCallEvent` hook to pause before sensitive actions
- **Structured output** — `GetStructuredOutputAsync<T>()` extracts typed records with automatic JSON retry
- **Session management** — `InMemorySessionManager` or `FileSessionManager`; bring your own via `ISessionManager`
- **Context window trimming** — `SlidingWindowStrategy` or `SummarizingConversationManager` for long-running agents
- **OpenTelemetry** — `ActivitySource` named `"Jacquard.Agent"` emits traces and metrics with zero config
- **DI integration** — `AddBedrockModel()`, `AddAnthropicModel()`, `AddOpenAICompatibleModel()`, `AddGeminiModel()`, `AddJacquardAgent()`, `AddJacquardToolProvider<T>()` for native ASP.NET Core / Worker Service wiring
- **Multi-agent graph** — `GraphBuilder` with conditional routing; `PipelineOrchestrator`; `ParallelOrchestrator`
- **Agent as tool** — wrap any `IAgent` as an `ITool` with `agent.AsTool()` for hierarchical orchestration
- **MCP** — connect any Model Context Protocol server (stdio or SSE) via `McpToolProvider`
- **A2A protocol** — expose agents over HTTP with `MapA2AEndpoint`; call remote agents with `A2AAgent` (cross-framework, cross-language)
- **AgentCore Runtime** *(optional)* — `MapAgentCoreEndpoints()` deploys any agent to Amazon Bedrock AgentCore Runtime in one line; `UseAgentCorePort(8080)` binds the required port
- **AgentCore Memory** *(optional)* — `AgentCoreMemoryTool` / `AddAgentCoreMemory()` gives the agent explicit store/retrieve/delete access to Amazon Bedrock AgentCore Memory; `AddAgentCoreSessionManager()` persists conversation sessions to the same store
- **AgentCore Code Interpreter** *(optional)* — `AgentCoreCodeInterpreterTool` / `AddAgentCoreCodeInterpreter()` executes Python, JavaScript, or TypeScript in a managed, stateful sandbox; session is created lazily and reused across calls
- **AgentCore Browser** *(optional)* — `AgentCoreBrowserTool` / `AddAgentCoreBrowser()` manages a headless Chrome session; returns the CDP `automationStreamEndpoint` for Playwright or Nova Act automation
- **AgentCore Gateway** *(optional)* — `AgentCoreGatewayToolProvider` / `AddAgentCoreGatewayTools()` connects to an Amazon Bedrock AgentCore Gateway MCP endpoint and exposes its tools as `ITool` instances; supports IAM SigV4, JWT Bearer, and network-isolated (no-auth) modes

---

## What native means in practice

These aren't translations — they're the patterns .NET developers already know, applied to agentic AI.

| Capability | Jacquard.NET |
| --- | --- |
| Type safety | Compile-time generics |
| Streaming | `IAsyncEnumerable<T>` |
| Hook registration | `Register<TEvent>` — compiler-checked |
| Tool schema | Roslyn source generator at compile time |
| Tool registration | `toolProviders: [new MyTools()]` — no generated type names |
| Parallel execution | `Task.WhenAll` |
| DI integration | `AddBedrockModel()` + `AddJacquardAgent()` + `AddJacquardToolProvider<T>()` |
| Enterprise hosting | `IHostedService` / AWS Lambda / any host |
| Model providers | Bedrock, Anthropic, OpenAI-compatible, Gemini |
| MCP | ✓ |
| A2A protocol | ✓ (interoperable across languages and frameworks) |
| Graph orchestration | ✓ with parallel-node support |
| NativeAOT | ✓ (89.6ms avg cold-start on AWS Lambda arm64) |

---

## Multi-agent patterns

### Sequential pipeline

```csharp
var pipeline = new PipelineOrchestrator([researchAgent, writerAgent, reviewerAgent]);
var result = await pipeline.RunAsync("Write a report on quantum computing");
```

### Parallel fan-out

```csharp
var results = await new ParallelOrchestrator([techAgent, marketAgent, riskAgent])
    .RunAsync("Analyse this topic from your specialist perspective");
// All three run concurrently via Task.WhenAll
```

### Graph with conditional routing

```csharp
var graph = new GraphBuilder()
    .AddNode("triage",    triageAgent)
    .AddNode("billing",   billingAgent)
    .AddNode("technical", techAgent)
    .AddConditionalEdge("triage", r =>
        r.Message.Contains("billing") ? "billing" : "technical")
    .Build();
```

### Agent as tool

```csharp
var researchTool = researchAgent.AsTool("researcher", "Research a topic and return a summary");
var writerAgent  = new Agent(model, tools: [researchTool]);
```

---

## AgentCore Gateway (optional)

Connect your agent to tools hosted on an Amazon Bedrock AgentCore Gateway — a managed MCP endpoint that proxies external APIs, databases, and services with built-in auth and observability.

```
dotnet add package Jacquard.Runtime
```

```csharp
// Direct usage — connect and list tools
await using var gateway = await AgentCoreGatewayToolProvider.CreateAsync(
    gatewayUrl: new Uri("https://...gateway-url.../mcp"),
    auth: new AgentCoreGatewayAuth.Iam(region: "us-east-1"));

var tools = await gateway.ListToolsAsync();
var agent = new Agent(model, tools: tools);
```

Three auth modes match your gateway's inbound authorization setting:

```csharp
// IAM SigV4 — credentials resolved from the standard AWS chain
new AgentCoreGatewayAuth.Iam(region: "us-east-1")

// JWT Bearer — Cognito, Entra ID, Okta, Google, GitHub, etc.
new AgentCoreGatewayAuth.Bearer(accessToken: token)

// No auth — network-isolated (VPC / security groups)
new AgentCoreGatewayAuth.None()
```

With DI, `AddAgentCoreGatewayTools()` registers all gateway tools directly into the container — `AddJacquardAgent()` picks them up automatically:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreGatewayTools(gatewayUrl, auth: new AgentCoreGatewayAuth.Iam("us-east-1"))
    .AddJacquardAgent();
```

---

## AgentCore Runtime deployment (optional)

Deploy any Jacquard.NET agent to Amazon Bedrock AgentCore Runtime with one line. Your agent code is unchanged.

```
dotnet add package Jacquard.Runtime
```

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddJacquardAgent();

var app = builder.Build();
app.MapAgentCoreEndpoints();  // POST /invocations + GET /health
app.UseAgentCorePort(8080);   // AgentCore Runtime expects port 8080
app.Run();
```

Optionally wire in managed AgentCore services before building the app:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddAgentCoreSessionManager(memoryId)
    .AddAgentCoreMemory(memoryId)
    .AddAgentCoreCodeInterpreter()
    .AddAgentCoreBrowser()
    .AddJacquardAgent();
```

---

## Packages

| Package | Description |
| --- | --- |
| `Jacquard.Core` | Agent, event loop, tool system, hooks, session management, Gemini/Anthropic/OpenAI models |
| `Jacquard.Models.Bedrock` | Amazon Bedrock model provider (Converse API) |
| `Jacquard.Tools` | Built-in tools: calculator, file read/write, HTTP request |
| `Jacquard.SourceGenerator` | Roslyn source generator — emits `ITool` wrappers and `IToolProvider` from `[Tool]` attributes |
| `Jacquard.Extensions.DI` | ASP.NET Core / Worker Service DI extensions |
| `Jacquard.MultiAgent` | Pipeline, parallel, and graph orchestration; A2A protocol |
| `Jacquard.Runtime` | Amazon Bedrock AgentCore Runtime hosting; managed Memory, Code Interpreter, Browser, and Gateway tools |

---

## Samples

| Sample | What it shows |
| --- | --- |
| [CliAgent](samples/CliAgent) | Multi-turn streaming REPL — the minimal working agent |
| [QuickstartSample](samples/QuickstartSample) | Built-in + custom tools, streaming — the canonical getting-started example |
| [AspNetAgent](samples/AspNetAgent) | `/chat` endpoint with session continuity and SSE streaming |
| [DiAgent](samples/DiAgent) | Full DI wiring with file tools and session management |
| [FileAgent](samples/FileAgent) | `FileReadTool` / `FileWriteTool` + `SlidingWindowStrategy` context trimming |
| [AutoTrimAssistant](samples/AutoTrimAssistant) | Zero-boilerplate auto-trim via `IAutoTrimConversationManager`; file session with TTL |
| [MultiAgentPipeline](samples/MultiAgentPipeline) | Sequential pipeline + parallel fan-out with timestamps |
| [OrchestratedResearch](samples/OrchestratedResearch) | All three orchestration patterns side by side |
| [SupportTriage](samples/SupportTriage) | Graph routing, hooks, and structured output extraction |
| [CustomerServiceApi](samples/CustomerServiceApi) | Production-shaped REST API with session persistence |
| [FinanceAssistant](samples/FinanceAssistant) | 4-agent parallel swarm with typed report extraction |
| [SwarmResearch](samples/SwarmResearch) | Dynamic swarm — 4 agents collaborate via autonomous handoffs; `SwarmOrchestrator.StreamAsync` with live console output |
| [SwarmResearchWeb](samples/SwarmResearchWeb) | Same swarm pattern as a web app — SSE endpoint + dark-theme browser UI with real-time agent pipeline and streaming text |
| [PersistentAssistant](samples/PersistentAssistant) | Cross-run memory with automatic summarization |
| [DistributedAgents](samples/DistributedAgents) | A2A cross-process agent communication |
| [ChatUI](samples/ChatUI) | Browser chat UI with SSE streaming and tool badges |
| [BlazorResearch](samples/BlazorResearch) | Blazor Server portal with live parallel agent cards |
| [ResponsibleAiSample](samples/ResponsibleAiSample) | Bedrock Guardrails, `[ToolParameterValidation]`, audit logging, least-privilege tool design |
| [AotLambda](samples/AotLambda) | NativeAOT publish to AWS Lambda — 89.6ms avg cold-start (arm64 Graviton2, 14 MB binary) |
| [DurableWorkflow](samples/DurableWorkflow) | Decomposed Sequential Pipeline pattern — agent durability inside each invocation, workflow durability between invocations via Step Functions |
| [CodeInterpreterSample](samples/CodeInterpreterSample) | `AgentCoreCodeInterpreterTool` — stateful Python / JS / TS sandbox via AgentCore |
| [BrowserSample](samples/BrowserSample) | `AgentCoreBrowserTool` — managed headless Chrome session; CDP endpoint for Playwright / Nova Act |
| [SemanticMemorySample](samples/SemanticMemorySample) | `SemanticMemoryTool` — vector / semantic search over AgentCore Memory |
| [AgentCoreSample](samples/AgentCoreSample) | Deploy any agent to AgentCore Runtime — `MapAgentCoreEndpoints()` in one line |
| [AgentCoreGatewaySample](samples/AgentCoreGatewaySample) | Travel booking assistant using gateway-hosted tools via `AddAgentCoreGatewayTools()` |

---

## About

Jacquard.NET is a native C# SDK for building agentic AI, inspired by the [Strands Agents](https://strandsagents.com) design principles, independently maintained. The core concepts — model-driven event loop, tool system, hooks, multi-agent orchestration — are implemented ground-up in C# 13, with full interoperability via MCP and A2A.

Strands Agents is an open source SDK from AWS that takes a model-driven approach to building AI agents. The design principles that emerged from that work are sound and worth bringing natively to the .NET ecosystem. Jacquard.NET is that native implementation — not a port, not a wrapper, not a language bridge. Built using the types and patterns .NET developers already know.

This project is not affiliated with or endorsed by AWS.

---

## Contributing

PRs, issues, and feedback are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines. The biggest areas of need are additional model providers (Ollama), more built-in tools, and real-world samples. Start a thread in [Discussions](../../discussions) before opening a large PR.

---

## License

Apache 2.0. See [LICENSE](LICENSE).