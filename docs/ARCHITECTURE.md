# Strands Agents .NET — Architecture & Implementation Reference

> This document is the authoritative reference for the Strands.NET SDK. It covers what the project is, every component built, how each part is implemented, and what is tested. Intended for contributors, reviewers, and anyone picking up the codebase for the first time.

---

## Table of Contents

1. [What is Strands Agents .NET?](#1-what-is-strands-agents-net)
2. [Package Structure](#2-package-structure)
3. [Core Concepts](#3-core-concepts)
4. [Jacquard.Core — The Engine](#4-strandscore--the-engine)
5. [Jacquard.SourceGenerator — Compile-Time Tool Schemas](#5-strandssourcegenerator--compile-time-tool-schemas)
6. [Jacquard.Models.Bedrock — AWS Bedrock Provider](#6-strandsmodelsbedrock--aws-bedrock-provider)
7. [Jacquard.Tools — Built-In Tools](#7-strandstools--built-in-tools)
8. [Jacquard.MultiAgent — Orchestration Patterns](#8-strandsmultiagent--orchestration-patterns)
9. [Jacquard.Extensions.DI — Dependency Injection](#9-strandsextensionsdi--dependency-injection)
10. [Jacquard.Runtime — AgentCore Runtime Hosting](#10-strandsagentcore--agentcore-runtime-hosting)
11. [Cross-Cutting Implementation Patterns](#11-cross-cutting-implementation-patterns)
12. [Test Coverage](#12-test-coverage)
13. [Samples](#13-samples)
14. [Build Configuration](#14-build-configuration)

---

## 1. What is Strands Agents .NET?

Strands Agents .NET is a model-driven agentic AI framework for C# developers. It implements the same architecture as [AWS Strands Agents](https://strandsagents.com) — event loop, tool system, hooks, multi-agent orchestration — using native .NET 10 and C# 13 patterns throughout.

**The core idea is simple:** you give the agent a model (any LLM), some tools (any callable methods), and a prompt. The agent's event loop calls the model, executes whatever tools it requests, feeds results back, and repeats until the model says it's done. The developer never writes the orchestration loop — the framework runs it.

```csharp
var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    tools: [new CalculatorTool_Calculate_Tool(new CalculatorTool())]);

var result = await agent.InvokeAsync("What is 42 multiplied by 1764?");
// The model called the calculator tool; result.Message = "The answer is 74,088"
```

### What makes this a .NET implementation, not a port

| Concept | Python Strands | Strands Agents .NET |
|---|---|---|
| Streaming | `async_generator` | `IAsyncEnumerable<StreamEvent>` |
| Tool schema generation | `inspect.signature()` at runtime | Roslyn source generator at compile time |
| Hook registration | String event names | `Register<TEvent>()` — compiler-checked |
| Parallel tool execution | `asyncio.gather` | `Task.WhenAll` |
| Type safety | Runtime type hints | Compile-time generics and records |
| DI integration | Manual wiring | `AddBedrockModel()` + `AddStrandsAgent()` |
| Multi-agent routing | Python conditionals | `GraphBuilder.AddConditionalEdge()` |
| AgentCore hosting | `BedrockAgentCoreApp()` + `@app.entrypoint` | `MapAgentCoreEndpoints()` |

---

## 2. Package Structure

```
src/
  Jacquard.Core                  Core engine: event loop, agent, hooks, conversation, sessions
  Jacquard.SourceGenerator       Roslyn IIncrementalGenerator for [Tool] attribute
  Jacquard.Models.Bedrock        AWS Bedrock via ConverseAsync / ConverseStreamAsync
  Jacquard.Tools                 Built-in tools: Calculator, FileRead, FileWrite, HttpRequest
  Jacquard.MultiAgent            Orchestration: Pipeline, Parallel, Graph, A2A
  Jacquard.Extensions.DI         IServiceCollection extension methods
  Jacquard.Runtime             AgentCore Runtime hosting + managed service tools

tests/
  Jacquard.Core.Tests            Unit tests — mocked IModel, no live endpoints
  Jacquard.Integration.Tests     Live Bedrock tests (gated by STRANDS_INTEGRATION_TESTS=true)
  Jacquard.Runtime.Tests       Unit tests for hosting, tools, session, DI

samples/
  CliAgent                      Multi-turn streaming REPL
  AspNetAgent                   SSE /chat endpoint with per-session isolation
  MultiAgentPipeline            Sequential pipeline + parallel fan-out
  OrchestratedResearch          All three orchestration patterns
  SupportTriage                 Graph routing, hooks, structured output
  CustomerServiceApi            Production REST API with file session persistence
  FinanceAssistant              4-agent parallel swarm with typed report extraction
  PersistentAssistant           Cross-run memory with automatic summarization
  DistributedAgents/            A2A cross-process communication (ResearchService + WriterClient)
  ChatUI                        Browser chat UI with SSE + tool event badges
  BlazorResearch                Blazor Server portal with live parallel agent cards
  AgentCoreSample               Deploy any agent to AgentCore Runtime — one line
```

**Dependency graph** (arrows = "depends on"):

```
Jacquard.Runtime ──────────────────────────────────┐
Jacquard.Extensions.DI ─────────────────────────┐   │
Jacquard.MultiAgent ────────────────────────┐   │   │
Jacquard.Tools ─────────────────────────┐   │   │   │
Jacquard.Models.Bedrock ────────────┐   │   │   │   │
                                   └───┴───┴───┴───┴──▶ Jacquard.Core
                                                     ▶ Jacquard.SourceGenerator (as Analyzer)
```

---

## 3. Core Concepts

### The event loop

Every agent invocation runs this loop:

```
prompt ──▶ build ModelRequest ──▶ call IModel.InvokeAsync
                                         │
                             ┌───────────┴────────────┐
                         EndTurn                  ToolUse calls
                             │                        │
                         return result           execute tools
                                                      │
                                             append tool results
                                                      │
                                              loop back to model
```

The loop terminates when:
- The model returns `StopReason.EndTurn` with no tool calls
- `AgentConfig.MaxIterations` is reached (default 10)
- A hook sets `Interrupt = true`
- A `CancellationToken` is cancelled

### Model-driven architecture

The LLM decides *which* tools to call and *when* to stop. The SDK never hard-codes routing logic — the model drives the loop. This is what "model-driven" means.

### Everything is an interface

`IModel`, `IAgent`, `ITool`, `IConversationManager`, `ISessionManager`, `IContextWindowStrategy` — every major component is interface-backed. This enables:
- Swapping models without changing agent code
- Mocking in unit tests
- DI registration of any implementation

---

## 4. Jacquard.Core — The Engine

### Public interfaces

```csharp
// The agent — invoke for a result or stream for token-by-token output
public interface IAgent
{
    Task<AgentResult> InvokeAsync(string prompt, CancellationToken ct = default);
    IAsyncEnumerable<StreamEvent> StreamAsync(string prompt, CancellationToken ct = default);
}

// Any LLM provider
public interface IModel
{
    Task<ModelResponse> InvokeAsync(ModelRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, CancellationToken ct = default);
}

// Any callable tool
public interface ITool
{
    ToolDefinition Definition { get; }
    Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default);
}

// Conversation history strategy
public interface IConversationManager
{
    IReadOnlyList<Message> GetMessages();
    void Append(Message message);
    void Trim();
}

// Session persistence
public interface ISessionManager
{
    Task<AgentSession?> LoadAsync(string sessionId, CancellationToken ct = default);
    Task SaveAsync(string sessionId, AgentSession session, CancellationToken ct = default);
}

// Pluggable token-budget trimming
public interface IContextWindowStrategy
{
    Task<IList<Message>> TrimAsync(IList<Message> messages, int maxTokens, CancellationToken ct = default);
}
```

### Data model (all records)

```csharp
// Conversation message
public record Message(Role Role, IReadOnlyList<ContentBlock> Content);
public enum Role { User, Assistant }

// Content blocks — discriminated union via abstract record
public abstract record ContentBlock;
public record TextBlock(string Text) : ContentBlock;
public record ToolUseBlock(string Id, string Name, JsonElement Input) : ContentBlock;
public record ToolResultBlock(string ToolUseId, string Content, bool IsError) : ContentBlock;

// Model request/response
public record ModelRequest(
    IReadOnlyList<Message> Messages,
    string? SystemPrompt,
    IReadOnlyList<ToolDefinition> Tools,
    ModelParameters Parameters);

public record ModelResponse(
    string? TextContent,
    IReadOnlyList<ToolCall> ToolCalls,
    StopReason StopReason,
    TokenUsage Usage);

public record ToolCall(string Id, string Name, JsonElement Input);
public record ToolDefinition(string Name, string Description, JsonElement InputSchema);
public record ToolResult(string ToolCallId, string Content, bool IsError = false)
{
    public static ToolResult Success(string toolCallId, string content) => new(toolCallId, content);
    public static ToolResult Failure(string toolCallId, string error) => new(toolCallId, error, IsError: true);
}

// Final result
public record AgentResult(string Message, StopReason StopReason, TokenUsage Usage, AgentMetrics Metrics);
public record AgentMetrics(TimeSpan TotalLatency, int Iterations, int ToolCallCount, TokenUsage TotalUsage);
public record TokenUsage(int InputTokens, int OutputTokens)
{
    public int Total => InputTokens + OutputTokens;
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => ...;
    public static readonly TokenUsage Zero = new(0, 0);
}

// Session persistence
public record AgentSession(
    string SessionId,
    IReadOnlyList<Message> Messages,
    IReadOnlyDictionary<string, object?> State,
    DateTimeOffset LastUpdated);

// Stop reasons
public enum StopReason { EndTurn, ToolUse, MaxTokens, StopSequence, MaxIterations, Interrupted, Error }
```

### Streaming events

Two separate hierarchies — one for the agent boundary, one for the model boundary:

```csharp
// Agent-level stream events (what callers see)
public abstract record StreamEvent;
public record TextDeltaEvent(string Delta) : StreamEvent;
public record ToolCallStartEvent(string ToolName) : StreamEvent;
public record ToolCallResultEvent(string ToolCallId, ToolResult Result) : StreamEvent;
public record AgentCompleteEvent(AgentResult Result) : StreamEvent;

// Model-level stream events (internal, from IModel.StreamAsync)
public abstract record ModelStreamEvent;
public record TextDeltaModelEvent(string Delta) : ModelStreamEvent;
public record ToolCallStartModelEvent(string Id, string Name) : ModelStreamEvent;
public record ToolCallInputDeltaModelEvent(string Id, string Delta) : ModelStreamEvent;
public record ModelCompleteEvent(ModelResponse Response) : ModelStreamEvent;
```

### The Agent class

```csharp
public sealed class Agent : IAgent
{
    public Agent(
        IModel model,
        string? systemPrompt = null,
        IEnumerable<ITool>? tools = null,
        IConversationManager? conversationManager = null,
        ISessionManager? sessionManager = null,
        HookRegistry? hooks = null,
        AgentConfig? config = null);

    public AgentState State { get; }

    public Task<AgentResult> InvokeAsync(string prompt, CancellationToken ct = default);
    public IAsyncEnumerable<StreamEvent> StreamAsync(string prompt, CancellationToken ct = default);
    public Task<T> GetStructuredOutputAsync<T>(string prompt, CancellationToken ct = default);
}
```

`GetStructuredOutputAsync<T>` appends the JSON schema for `T` to the system prompt, invokes the agent, and deserialises the response. On `JsonException` it retries up to 3 times, feeding the error message and raw response back to the model.

**AgentConfig:**
```csharp
public record AgentConfig
{
    public int MaxIterations { get; init; } = 10;
    public bool ParallelToolExecution { get; init; } = true;
    public IContextWindowStrategy? ContextWindowStrategy { get; init; } = null;
    public int MaxContextTokens { get; init; } = 100_000;
}
```

### AgentState

Thread-safe key/value bag that persists across turns within a session:

```csharp
public sealed class AgentState
{
    public T? Get<T>(string key);
    public void Set<T>(string key, T value);
    public bool Remove(string key);
    public bool ContainsKey(string key);
    public IReadOnlyDictionary<string, object?> ToSnapshot();
    public void Restore(IReadOnlyDictionary<string, object?> snapshot);
}
```

State is serialised to JSON when saved in `AgentSession.State`. Values come back as `JsonElement` after a round-trip — callers must use `Get<T>` to deserialise.

### ToolRegistry

The registry owns tool lookup and execution. It is thread-safe (uses `ConcurrentDictionary`):

```csharp
public sealed class ToolRegistry
{
    public void Register(ITool tool);
    public void RegisterAll(IEnumerable<ITool> tools);
    public ITool? Resolve(string name);          // case-insensitive
    public IReadOnlyList<ToolDefinition> GetDefinitions();
    public Task<IReadOnlyList<ToolResult>> ExecuteAsync(
        IEnumerable<ToolCall> calls, bool parallel, CancellationToken ct = default);
}
```

When `parallel = true` (from `AgentConfig.ParallelToolExecution`), all tool calls in a turn run via `Task.WhenAll`. The registry overwrites each `ToolResult.ToolCallId` with the model-assigned `call.Id` using `with { ToolCallId = call.Id }` — this is the canonical ID that binds tool results back to tool uses in the conversation.

### Hook system

```csharp
public sealed class HookRegistry
{
    public void Register<TEvent>(Func<TEvent, Task> handler) where TEvent : HookEvent;
    public Task FireAsync<TEvent>(TEvent evt, CancellationToken ct = default) where TEvent : HookEvent;
}

// Hook events — abstract record hierarchy
public abstract record HookEvent;
public record BeforeModelCallEvent(ModelRequest Request) : HookEvent  { public bool Interrupt { get; set; } }
public record AfterModelCallEvent(ModelRequest Request, ModelResponse Response) : HookEvent;
public record BeforeToolCallEvent(ToolCall Call) : HookEvent          { public bool Interrupt { get; set; } }
public record AfterToolCallEvent(ToolCall Call, ToolResult Result) : HookEvent;
```

Setting `BeforeToolCallEvent.Interrupt = true` causes the loop to stop after executing the tool but before the next model call — useful for human-in-the-loop review.

### Conversation managers

| Class | Behaviour |
|---|---|
| `InMemoryConversationManager` | Full history, unlimited. Default. |
| `SlidingWindowConversationManager(int maxMessages)` | Keeps only the last N messages. Trims synchronously. |
| `SummarizingConversationManager(IModel, int threshold, int keepRecentCount)` | Calls the model to summarise old messages when count exceeds threshold. `TrimAsync()` must be called explicitly; synchronous `Trim()` is a no-op. |
| `NullConversationManager` | Stateless. No history. Single-turn only. |

### Session managers

| Class | Behaviour |
|---|---|
| `InMemorySessionManager` | `ConcurrentDictionary<string, AgentSession>`. Not persisted across process restarts. |
| `FileSessionManager(string directory)` | JSON file per session at `{directory}/{sessionId}.json`. Creates directory on construction. |

### Context window strategy

If `AgentConfig.ContextWindowStrategy` is set, the event loop calls `TrimAsync` before each model call when the accumulated message list exceeds `AgentConfig.MaxContextTokens`. Two strategies ship with the SDK:
- `SlidingWindowConversationManager` effectively provides sliding-window trimming
- `SummarizingConversationManager` provides LLM-driven summarisation

Custom implementations just need to implement `IContextWindowStrategy`.

### Exceptions

```
StrandsException (abstract)
├── ModelException        — IModel call failed; carries HttpStatusCode
├── StructuredOutputException — GetStructuredOutputAsync<T> failed after 3 retries; carries RawResponse
└── ToolException         — tool execution threw; carries ToolName + ToolCallId
```

All exceptions carry `ConversationSnapshot` — a copy of messages at the time of failure, useful for debugging.

### Telemetry

`StrandsTelemetry` (internal) provides an OpenTelemetry `ActivitySource` named `"Strands.Agent"` and meters:
- `strands.agent.tokens.input` / `strands.agent.tokens.output` (counters)
- `strands.agent.latency` (histogram, milliseconds)
- `strands.agent.tool_calls` (counter with `tool.name` and `tool.success` tags)

Zero-config — wires automatically when an OpenTelemetry listener is present.

---

## 5. Jacquard.SourceGenerator — Compile-Time Tool Schemas

`ToolGenerator` is a Roslyn `IIncrementalGenerator`. It runs at compile time, not at runtime.

### How it works

1. Detects all methods decorated with `[Tool("description")]`
2. For each method, reads parameter names and types
3. Maps C# types to JSON Schema types:
   - `string` → `"string"`
   - `int`, `long` → `"integer"`
   - `double`, `float`, `decimal` → `"number"`
   - `bool` → `"boolean"`
4. Emits a `{ClassName}_{MethodName}_Tool : ITool` class with:
   - A `ToolDefinition` property containing the name, description, and embedded JSON schema
   - An `InvokeAsync(JsonElement input, CancellationToken ct)` method that deserialises each argument from the JSON input and calls the original method
   - If the original method has a `CancellationToken` parameter, it is forwarded automatically and excluded from the JSON schema
5. Generated classes are `public sealed`

### Example

```csharp
// Decorated method
public sealed class CalculatorTool
{
    [Tool("Performs basic arithmetic on two numbers.")]
    public double Calculate(double a, string operation, double b) { ... }
}

// Source generator emits:
public sealed class CalculatorTool_Calculate_Tool : ITool
{
    private readonly CalculatorTool _instance;
    public CalculatorTool_Calculate_Tool(CalculatorTool instance) => _instance = instance;

    public ToolDefinition Definition { get; } = new(
        Name: "Calculate",
        Description: "Performs basic arithmetic on two numbers.",
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "a":         { "type": "number" },
                "operation": { "type": "string" },
                "b":         { "type": "number" }
              },
              "required": ["a", "operation", "b"]
            }
        """).RootElement.Clone());

    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        var a         = input.GetProperty("a").GetDouble();
        var operation = input.GetProperty("operation").GetString()!;
        var b         = input.GetProperty("b").GetDouble();
        var result    = _instance.Calculate(a, operation, b);
        return ToolResult.Success(Definition.Name, result.ToString());
    }
}
```

The schema is a compile-time string constant — no reflection at runtime.

### CancellationToken forwarding

If the original method signature includes `CancellationToken ct`:
- The parameter is **excluded** from the JSON schema (the model never sees it)
- The generated `InvokeAsync` appends `ct` to the call: `_instance.ProcessAsync(arg1, ct)`

---

## 6. Jacquard.Models.Bedrock — AWS Bedrock Provider

```csharp
public sealed class BedrockModel : IModel, IDisposable
{
    public BedrockModel(
        string region = "us-east-1",
        string modelId = "us.anthropic.claude-sonnet-4-20250514-v1:0",
        AmazonBedrockRuntimeConfig? config = null,
        IAmazonBedrockRuntime? clientOverride = null);
}
```

### Implementation details

**Non-streaming** (`InvokeAsync`):
- Calls `IAmazonBedrockRuntime.ConverseAsync`
- Maps `Message[]` → Bedrock `Message[]` (including `ToolResultBlock` → `ToolResult` content)
- Maps Bedrock `ConverseResponse` → `ModelResponse`
- Retry policy: up to 3 attempts with exponential backoff + jitter on `ThrottlingException`, `ServiceUnavailableException`, HTTP 429

**Streaming** (`StreamAsync`):
- Calls `IAmazonBedrockRuntime.ConverseStreamAsync`
- The AWS SDK returns a sync event stream; the model bridges it to `IAsyncEnumerable` via `System.Threading.Channels`
- Events: `ContentBlockStart` (tool use start) → `ToolCallStartModelEvent`, `ContentBlockDelta` (text or tool input delta) → `TextDeltaModelEvent` / `ToolCallInputDeltaModelEvent`, `MessageStop` → `ModelCompleteEvent`

**Tool call ID ownership:** The Bedrock response assigns IDs to tool uses. These are passed through to `ToolCall.Id`. The `ToolRegistry` later overwrites `ToolResult.ToolCallId` with the same ID when constructing tool result messages.

---

## 7. Jacquard.Tools — Built-In Tools

### CalculatorTool

Decorated with `[Tool]`, schema generated at compile time:

```csharp
public sealed class CalculatorTool
{
    [Tool("Performs basic arithmetic (add, subtract, multiply, divide) on two numbers.")]
    public double Calculate(double a, string operation, double b);
}
```

Accepts: `add`/`+`, `subtract`/`-`, `multiply`/`*`/`x`, `divide`/`/`. Throws `DivideByZeroException` on divide by zero (the generated wrapper catches this and returns `ToolResult.Failure`).

### FileReadTool

Manual `ITool` implementation (no source generator — the schema is more complex):

```csharp
public sealed class FileReadTool : ITool
{
    public const int DefaultMaxSizeBytes = 1 * 1024 * 1024; // 1 MiB
    public FileReadTool(string allowedBasePath, int maxSizeBytes = DefaultMaxSizeBytes);
}
```

Security: resolves the requested path and verifies it starts with `allowedBasePath` after normalisation. Rejects path traversal (`../../etc/passwd`). Returns file contents as a string, or `ToolResult.Failure` if the path is outside the allowed base or the file exceeds `maxSizeBytes`.

### FileWriteTool

```csharp
public sealed class FileWriteTool : ITool
{
    public const int DefaultMaxContentBytes = 1 * 1024 * 1024; // 1 MiB
    public FileWriteTool(string allowedBasePath, int maxContentBytes = DefaultMaxContentBytes);
}
```

Same path traversal protection as `FileReadTool`. Creates parent directories automatically. Supports `append: true` for appending rather than overwriting.

### HttpRequestTool

```csharp
public sealed class HttpRequestTool : ITool
{
    public const string HttpClientName = "StrandsHttpRequestTool";
    public HttpRequestTool(IHttpClientFactory httpClientFactory);
}
```

Accepts GET or POST. Supports optional headers and body. Returns `{ statusCode, body }` as a JSON string. Uses a named `HttpClient` to participate in the `IHttpClientFactory` lifecycle (connection pooling, DNS refresh).

---

## 8. Jacquard.MultiAgent — Orchestration Patterns

### PipelineOrchestrator — sequential

Each stage's output text becomes the next stage's prompt:

```csharp
var pipeline = new PipelineOrchestrator([researchAgent, writerAgent, reviewerAgent]);
var result = await pipeline.RunAsync("Write a report on quantum computing");

// Streaming: each event is tagged with which stage produced it
await foreach (var stageEvent in pipeline.StreamAsync("..."))
{
    if (stageEvent.Event is TextDeltaEvent delta)
        Console.Write($"[Stage {stageEvent.StageIndex}] {delta.Delta}");
}
```

`PipelineStageEvent` wraps a `StreamEvent` with `StageIndex` and optional `StageName`.

### ParallelOrchestrator — fan-out

All agents run with the same prompt via `Task.WhenAll`:

```csharp
var results = await new ParallelOrchestrator([techAgent, marketAgent, riskAgent])
    .RunAsync("Analyse this topic from your specialist perspective");
// results[0] = tech analysis, results[1] = market analysis, results[2] = risk analysis
```

### GraphBuilder — conditional routing

Builds a directed graph of agents with conditional or unconditional edges:

```csharp
var graph = new GraphBuilder()
    .AddNode("triage",    triageAgent)
    .AddNode("billing",   billingAgent)
    .AddNode("technical", techAgent)
    .AddConditionalEdge("triage", r =>
        r.Message.Contains("billing") ? "billing" : "technical")
    .Build();

var result = await graph.RunAsync("I have a billing question");
```

Implementation:
- `Build()` validates that all edge targets reference existing nodes
- `RunAsync` starts at the first node added, follows edges, stops when a node has no outgoing edge or selects `GraphBuilder.End`
- A 50-iteration cap prevents infinite cycles
- Edge types: `UnconditionalEdge(string To)`, `ConditionalEdge(Func<AgentResult, string> Selector)` — abstract record hierarchy

### AgentTool — agent as tool

Wraps any `IAgent` as an `ITool` so a parent agent can delegate to it:

```csharp
var researchTool = researchAgent.AsTool("researcher", "Research a topic and return a summary");
var writerAgent  = new Agent(model, tools: [researchTool]);
// The writer will call the researcher as a tool
```

Input schema: `{ "type": "object", "properties": { "prompt": { "type": "string" } }, "required": ["prompt"] }`

### A2A protocol — cross-process, cross-language

**Server side** — expose an agent as an HTTP service:
```csharp
app.MapA2AEndpoint("/agent", agentInstance);
```

**Client side** — call a remote agent as if it were local:
```csharp
var remote = new A2AAgent("https://other-service/agent");
var result = await remote.InvokeAsync("Do some research");
```

`A2AAgent` implements `IAgent`, so it can be used anywhere a local agent is used — including as an `AgentTool` in a multi-agent graph. It implements `IDisposable` and only disposes the `HttpClient` when it created it (not when a `clientOverride` was injected).

---

## 9. Jacquard.Extensions.DI — Dependency Injection

All methods extend `IServiceCollection` and return `IServiceCollection` for fluent chaining.

### Model registration (singleton)

```csharp
services.AddBedrockModel(region: "us-east-1", modelId: "...");
services.AddAnthropicModel(apiKey: "...", modelId: "claude-sonnet-4-5");
services.AddOpenAICompatibleModel(baseUrl: "...", apiKey: "...", modelId: "gpt-4o");
```

### Agent registration (transient)

```csharp
services.AddStrandsAgent(config: new AgentConfig { MaxIterations = 20 });
```

Resolves `IModel` (required), `IEnumerable<ITool>` (optional), `IConversationManager` (optional), `ISessionManager` (optional), `HookRegistry` (optional) from DI. Note: `systemPrompt` is not set via DI — construct `Agent` directly when a system prompt is needed per-session.

### Tool registration (accumulates as IEnumerable<ITool>)

```csharp
services.AddStrandsTool<CalculatorTool_Calculate_Tool>();  // generic
services.AddHttpRequestTool();                              // adds named HttpClient too
services.AddFileReadTool(allowedBasePath: "/data");
services.AddFileWriteTool(allowedBasePath: "/data");
```

Multiple tool registrations stack — all are resolved together via `IEnumerable<ITool>` in `AddStrandsAgent`.

### Session manager (singleton)

```csharp
services.AddStrandsInMemorySessionManager();
// or construct directly:
services.AddSingleton<ISessionManager>(_ => new FileSessionManager("/sessions"));
```

---

## 10. Jacquard.Runtime — AgentCore Runtime Hosting

### What AgentCore is

AgentCore is a **hosting platform and set of managed services** from AWS, not a model or agent type. The correct mental model:

```
Your Strands.NET agent ──unchanged──▶
                                      MapAgentCoreEndpoints() ──▶ POST /invocations
                                                               ──▶ GET /health
                                                                        │
                                                               AgentCore Runtime routes traffic here
```

Nothing in `Jacquard.Core`, `Jacquard.Models.Bedrock`, or any other package changes. `Jacquard.Runtime` adds a hosting wrapper and optional managed service tools.

### Hosting wrapper

```csharp
// One line — your agent is now deployable to AgentCore Runtime
app.MapAgentCoreEndpoints();  // registers POST /invocations + GET /health
app.UseAgentCorePort(8080);   // AgentCore Runtime expects port 8080
```

**`POST /invocations`** (AgentCoreInvocationHandler):
- Request: `{ "prompt": "...", "sessionId": "...", "context": { ... } }`
- Non-streaming (default): resolves `IAgent` from DI, calls `InvokeAsync`, returns `{ "result": "...", "stopReason": "...", "usage": { ... } }`
- Streaming (`Accept: text/event-stream`): calls `StreamAsync`, emits SSE events:
  - `data: {"text": "token"}\n\n` for each `TextDeltaEvent`
  - `data: {"stopReason": "end_turn"}\n\ndata: [DONE]\n\n` on `AgentCompleteEvent`
  - Flushes after every write (AgentCore Runtime buffers until flush)

**`GET /health`** (AgentCoreHealthHandler):
- Returns `{ "status": "healthy", "framework": "Strands.NET", "timestamp": "..." }`
- Must return 200 before AgentCore Runtime routes any traffic

### Managed service tools

All three implement `ITool` and `IAsyncDisposable`. All use `HttpClient` internally (no dependency on AWS SDK packages that may not yet be available):

**AgentCoreMemoryTool** — explicit long-term memory:
```csharp
new AgentCoreMemoryTool(memoryId: "mem-id", region: "us-east-1")
// Operations: store_memory, retrieve_memory, delete_memory
```

**AgentCoreBrowserTool** — managed browser sandbox:
```csharp
new AgentCoreBrowserTool(region: "us-east-1")
// Operations: navigate, screenshot, extract_text, click, type
```

**AgentCoreCodeInterpreterTool** — managed code execution:
```csharp
new AgentCoreCodeInterpreterTool(region: "us-east-1")
// execute(code, language) → { stdout, stderr, exitCode }
// Supported languages: python, javascript, bash
```

### AgentCoreSessionManager

Implements `ISessionManager` backed by AgentCore Memory. Serialises the full `AgentSession` (messages + state) to a JSON record stored via PUT, retrieves via GET, returns `null` on 404.

```csharp
new AgentCoreSessionManager(memoryId: "mem-id", region: "us-east-1")
```

### DI extensions

```csharp
services.AddAgentCoreSessionManager(memoryId, region);  // ISessionManager
services.AddAgentCoreMemory(memoryId, region);           // ITool
services.AddAgentCoreBrowser(region);                    // ITool
services.AddAgentCoreCodeInterpreter(region);            // ITool
```

### Three customer journeys

```csharp
// Journey 1 — pure Strands.NET, no AgentCore
services.AddBedrockModel().AddStrandsAgent();

// Journey 2 — AgentCore managed services, any hosting
services
    .AddBedrockModel()
    .AddAgentCoreSessionManager(memoryId)
    .AddAgentCoreBrowser()
    .AddAgentCoreCodeInterpreter()
    .AddStrandsAgent();

// Journey 3 — full AgentCore: managed services + Runtime hosting
var app = builder.Build();
app.MapAgentCoreEndpoints();
app.UseAgentCorePort(8080);
app.Run();
```

### Dockerfile.agentcore template

AgentCore Runtime requires ARM64 containers. A template is provided at `src/Jacquard.Runtime/Dockerfile.agentcore`:

```dockerfile
FROM --platform=linux/arm64 mcr.microsoft.com/dotnet/aspnet:10.0
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
```

---

## 11. Cross-Cutting Implementation Patterns

### Async

- Every async method takes `CancellationToken ct = default` — no exceptions
- Every `await` in library code uses `ConfigureAwait(false)`
- `IAsyncEnumerable<T>` for all streaming; never `.Result`, `.Wait()`, or sync-over-async
- `[EnumeratorCancellation]` on every `IAsyncEnumerable` method's `CancellationToken` parameter
- `async void` is prohibited — all event-like methods return `Task`

### Records for value types

All data-carrying types are `record` for structural equality and `with` expression support. Discriminated unions use `abstract record` base types — code switches on type, never on a string tag.

### Primary constructors

Used on all `record` types and on `class` types whose constructor has no logic. Traditional constructors are reserved for cases where the constructor body needs to do work (e.g. creating a directory, validating arguments with side effects).

### Thread safety

- `ToolRegistry`: `ConcurrentDictionary<string, ITool>` — concurrent reads and writes are safe
- `HookRegistry`: `ConcurrentDictionary` for handler lists
- `InMemorySessionManager`: `ConcurrentDictionary<string, AgentSession>`
- `AgentState`: thread-safe via locking on its internal dictionary

### Client ownership pattern

All types that wrap an `HttpClient` or AWS SDK client follow the same ownership pattern:

```csharp
// When caller provides a client — the type does NOT dispose it
public AgentCoreBrowserTool(string region = "us-east-1", HttpClient? clientOverride = null)
{
    _ownsClient = clientOverride is null;
    _http = clientOverride ?? new HttpClient { BaseAddress = ... };
}

public async ValueTask DisposeAsync()
{
    if (_ownsClient) _http.Dispose();
    await ValueTask.CompletedTask.ConfigureAwait(false);
}
```

This enables test injection (`clientOverride: fakeClient`) without leaking resources in production.

### Warning-free build

`TreatWarningsAsErrors = true` in `Directory.Build.props`. Every compiler warning is a build error. `CA2024` is suppressed globally (false positive in .NET 10 with `using var` in async methods).

### XML documentation

All public types and members carry `<summary>`, `<param>`, and `<returns>` XML doc comments. Inline comments are reserved for *why* a non-obvious choice was made, never for *what* the code does.

---

## 12. Test Coverage

All tests use xUnit. Unit tests mock `IModel` with Moq. No unit test calls a live endpoint.

### Jacquard.Core.Tests (126 tests)

| Test class | What it covers |
|---|---|
| `EventLoopTests` | End-turn termination, tool_use → loop, max iterations, parallel tool execution, cancellation, hooks interrupting the loop |
| `ToolRegistryTests` | Registration, resolution (case-insensitive), sequential/parallel execution, unknown tool error, exception wrapping |
| `HookRegistryTests` | FIFO handler dispatch, interrupt flag on BeforeToolCallEvent, exception propagation, multiple event types, cancellation |
| `FileToolsTests` | FileReadTool (happy path, path traversal rejection, size limit), FileWriteTool (write, append, path traversal rejection) |
| `SourceGeneratorTests` | Generator emits ITool with correct name/description/schema; parameter type mapping; async method; void return; CancellationToken excluded from schema and forwarded to call |
| `SlidingWindowTests` | Message count respected; oldest messages trimmed first |
| `ContextWindowStrategyTests` | Strategy interface called before model; trimmed message list used |
| `SessionManagerTests` | InMemory round-trip; FileSession round-trip (write/read/delete); null on unknown session |
| `StructuredOutputTests` | Successful extraction; retry on JsonException with amended prompt; StructuredOutputException after 3 failures |
| `MultiAgentTests` | PipelineOrchestrator stage ordering; ParallelOrchestrator Task.WhenAll; GraphBuilder conditional routing |
| `ExceptionHierarchyTests` | Exception types and inheritance chain; ConversationSnapshot captured |
| `A2ARoundTripTests` | A2A request/response serialisation round-trip |
| `GraphBuilderTests` | Node/edge validation; conditional selector; unconditional edge; 50-iteration cycle guard |
| `StrandsDIExtensionsTests` | All DI registration methods; IModel/IAgent/ITool resolution |
| `HttpRequestToolTests` | GET request, POST with body, custom headers, error status code |
| `McpToolWrapperTests` | MCP tool provider wraps server tools as ITool |

### Jacquard.Runtime.Tests (31 tests)

| Test class | What it covers |
|---|---|
| `HostingTests` | `GET /health` returns 200 with correct body; `POST /invocations` calls agent with correct prompt; SSE streaming emits `data:` lines and `[DONE]`; missing prompt → 400; invalid JSON → 400; both routes registered; `UseAgentCorePort` adds URL |
| `ToolTests` | Each tool has correct name/description; InputSchema is valid JSON; missing/unknown operation returns error; missing required field returns error; clientOverride constructor path |
| `SessionTests` | 404 → returns null; 200 → reconstructs AgentSession with correct messages; SaveAsync sends PUT with sessionId in path; DisposeAsync disposes owned client |
| `DiTests` | `AddAgentCoreSessionManager` registers `AgentCoreSessionManager` as `ISessionManager`; each `AddAgentCore*` registers correct `ITool` type; all three tools registered together |

### Jacquard.Integration.Tests (3 tests)

Gated by `STRANDS_INTEGRATION_TESTS=true`. Run with:

```bash
STRANDS_INTEGRATION_TESTS=true dotnet test --filter "Category=Integration"
```

| Test | What it covers |
|---|---|
| `CalculatorGateTest` | Live Bedrock call with CalculatorTool; verifies model correctly uses the tool and returns numeric answer |

---

## 13. Samples

### CliAgent

**Demonstrates:** Multi-turn streaming REPL, `CancelKeyPress` for graceful shutdown, `TextDeltaEvent` delta printing, coloured console prompts.

```bash
cd samples/CliAgent && dotnet run
```

The agent persists conversation history in memory across turns. Ctrl+C cancels cleanly via `CancellationTokenSource`.

### AspNetAgent

**Demonstrates:** Per-session agent isolation via `ConcurrentDictionary<string, Agent>`, SSE streaming via `text/event-stream`, `DELETE /sessions/{id}`, `GET /health`.

```bash
cd samples/AspNetAgent && dotnet run
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{"sessionId": "abc", "message": "Hello"}'
```

### MultiAgentPipeline

**Demonstrates:** `PipelineOrchestrator` (Researcher → Writer → Reviewer in sequence), `ParallelOrchestrator` (technology + business analysts run simultaneously), `Stopwatch` timestamps proving simultaneous execution.

### OrchestratedResearch

**Demonstrates:** All three orchestration patterns — pipeline, parallel fan-out, and conditional graph routing — in a single program.

### SupportTriage

**Demonstrates:** `GraphBuilder` with `AddConditionalEdge`, `HookRegistry` with `BeforeToolCallEvent`, `GetStructuredOutputAsync<T>` for typed report extraction.

### CustomerServiceApi

**Demonstrates:** Production-shaped REST API with `FileSessionManager` (sessions survive process restart), per-session `Agent` construction with system prompt, structured error responses.

### FinanceAssistant

**Demonstrates:** 4-agent parallel swarm (market analyst, risk analyst, technical analyst, compliance analyst) via `ParallelOrchestrator`, typed `FinancialReport` extraction via `GetStructuredOutputAsync<T>`.

### PersistentAssistant

**Demonstrates:** `SummarizingConversationManager` for automatic context summarisation, `FileSessionManager` for cross-run persistence, explicit `TrimAsync()` call before long sessions.

### DistributedAgents

Two separate processes communicating via A2A protocol:
- `ResearchService` — exposes a research agent via `MapA2AEndpoint`
- `WriterClient` — creates an `A2AAgent` pointing at the research service, uses it as a tool

### ChatUI

**Demonstrates:** Browser-based chat UI, SSE streaming to `<div>` via `EventSource`, tool call badges in the UI, `POST /chat` + `GET /history/{sessionId}` endpoints.

### BlazorResearch

**Demonstrates:** Blazor Server with real-time UI updates via `StateHasChanged`, three parallel research agents updating independent cards simultaneously, `IAsyncEnumerable` streaming into Blazor components.

### AgentCoreSample

**Demonstrates:** The unchanged-agent pattern — the agent configuration block (BedrockModel + tools + session manager) is identical whether running locally or on AgentCore Runtime. `MapAgentCoreEndpoints()` + `UseAgentCorePort(8080)` are the only AgentCore-specific lines.

```bash
cd samples/AgentCoreSample && dotnet run

# Non-streaming
curl -X POST http://localhost:8080/invocations \
  -H "Content-Type: application/json" \
  -d '{"prompt": "What is 6 times 7?"}'

# Streaming
curl -X POST http://localhost:8080/invocations \
  -H "Content-Type: application/json" \
  -H "Accept: text/event-stream" \
  -d '{"prompt": "Explain recursion in 3 sentences"}'

# Health check
curl http://localhost:8080/health
```

---

## 14. Build Configuration

### Directory.Build.props (applies to all projects)

```xml
<TargetFramework>net10.0</TargetFramework>
<LangVersion>latest</LangVersion>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<NoWarn>CA2024</NoWarn>
<Version>0.1.0-preview.1</Version>
<Authors>Strands .NET Contributors</Authors>
<Company>Community</Company>
<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>
<PackageProjectUrl>https://github.com/apncodes/strands.net</PackageProjectUrl>
<RepositoryUrl>https://github.com/apncodes/strands.net</RepositoryUrl>
<PublishRepositoryUrl>true</PublishRepositoryUrl>
<EmbedUntrackedSources>true</EmbedUntrackedSources>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

### global.json

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestPatch" }
}
```

### GitHub Actions

**`ci.yml`** — triggers on push/PR to `main`:
1. `dotnet build --configuration Release` — must be 0 errors, 0 warnings
2. `dotnet test --configuration Release` — all tests must pass
3. `dotnet pack --configuration Release` — all packages must pack successfully

**`publish.yml`** — triggers on `v*` tags:
1. `dotnet pack --configuration Release --output ./artifacts`
2. `dotnet nuget push ./artifacts/*.nupkg --source nuget.org` using `NUGET_API_KEY` secret

### Running tests

```bash
# Unit tests only (fast, no AWS credentials needed)
dotnet test --configuration Release

# Integration tests (requires AWS credentials + STRANDS_INTEGRATION_TESTS=true)
STRANDS_INTEGRATION_TESTS=true dotnet test --filter "Category=Integration"

# Build and pack all packages
dotnet build --configuration Release
dotnet pack --configuration Release --output ./artifacts
```
