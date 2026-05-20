# Changelog

All notable changes to Strands Agents .NET are documented here.

---

## [Unreleased]

### Added

- **`SwarmOrchestrator`** (`Jacquard.MultiAgent`) — dynamic agent-driven handoff chain. Unlike `PipelineOrchestrator` (fixed sequence) or `ParallelOrchestrator` (fan-out/fan-in), a swarm has no predetermined execution path. Each agent receives the full task context — original request, agent history, shared knowledge from previous agents, and the list of available peers — then decides autonomously whether to hand off or terminate.

- **`SwarmOrchestrator.StreamAsync`** — `IAsyncEnumerable<SwarmEvent>` stream of typed lifecycle events. `RunAsync` is implemented by consuming `StreamAsync` internally — single source of truth for loop logic.

- **`SwarmEvent` hierarchy** — `SwarmStartedEvent`, `AgentStartedEvent`, `AgentTextDeltaEvent`, `AgentToolCallEvent`, `AgentToolResultEvent`, `AgentCompletedEvent`, `HandoffEvent`, `SwarmCompletedEvent`. Subscribe from any `IAsyncEnumerable` consumer: console, ASP.NET SSE endpoint, Blazor component, etc.

- **`SwarmAgentNode`** — named agent node with optional description used as routing hints for peer agents.

- **`SwarmResult`** / **`SwarmNodeResult`** — result types carrying final message, ordered node history, termination status (`Completed`, `MaxHandoffsReached`, `MaxIterationsReached`, `TimedOut`), and aggregate token usage.

- **Safety bounds** — `maxHandoffs`, `maxIterations`, `executionTimeout`, `nodeTimeout`, and ping-pong detection via `repetitiveHandoffDetectionWindow` / `repetitiveHandoffMinUniqueAgents`.

- **`samples/SwarmResearch`** — console sample: 4-agent research article swarm (researcher → analyst → writer → editor) with rich live output using the `SwarmEvent` stream.

- **`samples/SwarmResearchWeb`** — ASP.NET Core web sample: same swarm pattern with an SSE endpoint and a dark-theme browser UI showing real-time agent pipeline, activity log, streaming text, and final article tab.

---

## [0.1.10] — 2026-05-15

### Fixed

- **`Jacquard.SourceGenerator`** — Generated tool parameter deserialization now uses AOT-safe `JsonElement` accessors (`GetString()`, `GetInt32()`, `GetDouble()`, `GetBoolean()`) instead of `Deserialize<T>()` which requires runtime reflection. In AOT-published applications (e.g. Lambda `provided.al2023`), the old code silently returned `ToolResult.Failure` on every tool call. In JIT applications, `GetString()` is also the more correct and efficient API for this use case.

- **`Jacquard.Models.Bedrock`** — `DocumentToJson` now uses a manual `StringBuilder`-based JSON builder instead of `JsonSerializer.Serialize(Dictionary<string, object?>)`. The old code used polymorphic serialization requiring runtime type inspection, which fails in AOT contexts and generates IL2026/IL3050 trim warnings in all contexts.

### Added

- **`samples/AotLambda`** — New sample demonstrating a Strands Agents .NET agent published as a NativeAOT AWS Lambda function (`provided.al2023` runtime). Includes full benchmark results: ~118ms average cold-start init duration, 52 MB memory, 14 MB binary. See `samples/AotLambda/README.md` for test conditions and reproducibility instructions.

---

## [0.1.9] — 2026-05-15

### Changed

- **NuGet package metadata** — all seven packages now have accurate, package-specific descriptions, expanded tags (`mcp`, `a2a`, `source-generator`, `aot`), and correct repository URLs.
- **README polish** — fixed broken Jump-to anchor (`#why-strandsagentsnet`), retired the `Strands.NET` name variant, standardized comparison table to use `WeatherTools`, added Community section linking to GitHub Discussions.
- **Per-package READMEs** — fixed stale GitHub URLs; `Jacquard.Tools` README updated to use the modern `toolProviders:` pattern.

---

## [0.1.8] — 2026-05-14

### Added

- **`IToolProvider` pattern** — classes with `[Tool]`-decorated methods can now be passed directly to the `Agent` constructor without referencing source-generated wrapper type names. Declare the class `partial` and pass it via `toolProviders:`:

  ```csharp
  public partial class MyTools
  {
      [Tool("Returns weather")]
      public string GetWeather(string city) => $"Sunny in {city}";
  }

  var agent = new Agent(model, toolProviders: [new MyTools()]);
  ```

- **`IToolProvider` interface** (`Jacquard.Core`) — single contract implemented automatically by the source generator. Users never write `GetTools()` by hand.

- **`STRAND001` diagnostic** — the source generator emits a `Warning` when a class with `[Tool]` methods is not declared `partial`. The per-method wrapper classes are still emitted so existing code keeps compiling.

- **`Agent` constructor `toolProviders` parameter** — new optional `IEnumerable<IToolProvider>? toolProviders` parameter (positioned after `tools`). Both `tools` and `toolProviders` can be supplied simultaneously; all tools are merged into the same registry. All existing call sites compile unchanged.

- **`AddStrandsToolProvider<TProvider>()` DI extension** (`Jacquard.Extensions.DI`) — registers a tool-provider type as a transient `IToolProvider`. Multiple calls accumulate providers, all resolved by `AddStrandsAgent()`.

- **`CalculatorTool` declared `partial`** — enables `toolProviders: [new CalculatorTool()]` in user code and in `samples/CliAgent`.

### Changed

- `samples/CliAgent/Program.cs` updated to use `toolProviders: [new CalculatorTool()]` instead of the explicit wrapper form.
- README quickstart updated to show the `partial class` + `toolProviders:` pattern as the primary example, with the old wrapper form retained as a backward-compatibility note.

---

## [0.1.5] — 2025-05-09

### Fixed

- Package README files corrected — all `Strands.*` references updated to `Jacquard.*`
- `PackageProjectUrl` and `RepositoryUrl` updated to `https://github.com/apncodes/Jacquard.net`
- CI badge in root README updated to new repo URL
- `CONTRIBUTING.md` and `docs/ARCHITECTURE.md` updated to `Jacquard.*` names

---

## [0.1.4] — 2025-05-09

This release renames all packages from `Strands.*` to `Jacquard.*` to align with the project's brand at [strandsagents.com](https://strandsagents.com). The project was made public only days before this release, so the rename happens early — before any production adoption — to avoid a more disruptive migration later.

### New

- **`GeminiModel`** — Google Gemini support via the Gemini Developer REST API. No third-party SDK required; uses `HttpClient` + `System.Text.Json` consistent with `AnthropicModel` and `OpenAICompatibleModel`. Available in `Jacquard.Core`.

```csharp
var model = new GeminiModel(apiKey: "AIza...", modelId: "gemini-2.0-flash");

// DI
services.AddGeminiModel(apiKey: "AIza...", modelId: "gemini-2.0-flash");
```

### Changed

**Package IDs renamed** (pre-1.0 housekeeping — done early to avoid a larger migration later):

| Old | New |
|---|---|
| `Strands.Core` | `Jacquard.Core` |
| `Strands.Models.Bedrock` | `Jacquard.Models.Bedrock` |
| `Strands.Tools` | `Jacquard.Tools` |
| `Strands.SourceGenerator` | `Jacquard.SourceGenerator` |
| `Strands.Extensions.DI` | `Jacquard.Extensions.DI` |
| `Strands.MultiAgent` | `Jacquard.MultiAgent` |
| `Strands.AgentCore` | `Jacquard.Runtime` |

The old `Strands.*` packages remain on NuGet as deprecated compatibility shims that forward to the new names. They will stop receiving updates after **November 2025**.

**Namespaces renamed** to match package IDs:

```csharp
// Before
using Strands.Core;
using Strands.Models.Bedrock;
using Strands.MultiAgent;

// After
using Jacquard.Core;
using Jacquard.Models.Bedrock;
using Jacquard.MultiAgent;
```

**OpenTelemetry source and meter names renamed:**

```csharp
// Before
.AddSource("Strands.Agent")
.AddMeter("Strands.Agent")

// After
.AddSource("Jacquard.Agent")
.AddMeter("Jacquard.Agent")
```

Metric names: `strands.tokens.input` → `strandsagents.tokens.input`, `strands.tokens.output` → `strandsagents.tokens.output`, `strands.tool.calls` → `strandsagents.tool.calls`, `strands.agent.latency` → `strandsagents.agent.latency`.

---

## [0.1.3] — 2025-05-08

- Initial public release.
- Core agent, event loop, tool system, hooks, session management.
- Model providers: `BedrockModel`, `AnthropicModel`, `OpenAICompatibleModel`.
- Multi-agent patterns: pipeline, parallel, graph, A2A.
- AgentCore Runtime hosting and Gateway support.
- 14 sample projects.

---

## [0.1.0] – [0.1.2]

Pre-public development iterations.
