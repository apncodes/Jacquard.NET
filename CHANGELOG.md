# Changelog

All notable changes to Strands Agents .NET are documented here.

---

## [0.1.5] — 2025-05-09

### Fixed

- Package README files corrected — all `Strands.*` references updated to `StrandsAgents.*`
- `PackageProjectUrl` and `RepositoryUrl` updated to `https://github.com/apncodes/StrandsAgents.net`
- CI badge in root README updated to new repo URL
- `CONTRIBUTING.md` and `docs/ARCHITECTURE.md` updated to `StrandsAgents.*` names

---

## [0.1.4] — 2025-05-09

This release renames all packages from `Strands.*` to `StrandsAgents.*` to align with the project's brand at [strandsagents.com](https://strandsagents.com). The project was made public only days before this release, so the rename happens early — before any production adoption — to avoid a more disruptive migration later.

### New

- **`GeminiModel`** — Google Gemini support via the Gemini Developer REST API. No third-party SDK required; uses `HttpClient` + `System.Text.Json` consistent with `AnthropicModel` and `OpenAICompatibleModel`. Available in `StrandsAgents.Core`.

```csharp
var model = new GeminiModel(apiKey: "AIza...", modelId: "gemini-2.0-flash");

// DI
services.AddGeminiModel(apiKey: "AIza...", modelId: "gemini-2.0-flash");
```

### Changed

**Package IDs renamed** (pre-1.0 housekeeping — done early to avoid a larger migration later):

| Old | New |
|---|---|
| `Strands.Core` | `StrandsAgents.Core` |
| `Strands.Models.Bedrock` | `StrandsAgents.Models.Bedrock` |
| `Strands.Tools` | `StrandsAgents.Tools` |
| `Strands.SourceGenerator` | `StrandsAgents.SourceGenerator` |
| `Strands.Extensions.DI` | `StrandsAgents.Extensions.DI` |
| `Strands.MultiAgent` | `StrandsAgents.MultiAgent` |
| `Strands.AgentCore` | `StrandsAgents.Runtime` |

The old `Strands.*` packages remain on NuGet as deprecated compatibility shims that forward to the new names. They will stop receiving updates after **November 2025**.

**Namespaces renamed** to match package IDs:

```csharp
// Before
using Strands.Core;
using Strands.Models.Bedrock;
using Strands.MultiAgent;

// After
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.MultiAgent;
```

**OpenTelemetry source and meter names renamed:**

```csharp
// Before
.AddSource("Strands.Agent")
.AddMeter("Strands.Agent")

// After
.AddSource("StrandsAgents.Agent")
.AddMeter("StrandsAgents.Agent")
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
