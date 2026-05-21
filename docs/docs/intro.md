---
sidebar_position: 1
---

# Jacquard.NET

**Model-driven agentic AI for C# developers.**

Jacquard.NET brings the [Strands Agents](https://strandsagents.com) architecture to the .NET ecosystem — the same event loop, tool system, and multi-agent patterns, built ground-up in idiomatic C# 13.

Give an agent a model, tools, and a prompt. The event loop calls the model, executes whatever tools it requests, feeds results back, and repeats until the model signals it's done. You never write the orchestration loop.

The name comes from the Jacquard loom — invented in 1804, it was the first machine to use punch cards to control patterns. It inspired Charles Babbage and is the origin of the idea that a machine could be programmed to weave any pattern from simple instructions. That is what agents do: weave tools, models, loops, and prompts into intelligent behavior.

## Why Jacquard.NET

**The goal is that any .NET developer — from line-of-business engineer to senior architect — can read the quickstart and start building agents the same afternoon.** No prior agent experience required, no agent-framework vocabulary to learn, no orchestration loop to write.

Built around four principles: don't over-engineer, keep things clean, embrace open standards, be pragmatic about what to ship. The vocabulary matches what the broader agentic ecosystem has converged on. The protocols are open. The cloud integration is native where it makes sense, abstracted where it doesn't.

## Quick install

```bash
dotnet add package Jacquard.Core
dotnet add package Jacquard.SourceGenerator
# Add the model provider you want to use:
dotnet add package Jacquard.Models.Bedrock   # Amazon Bedrock
# Or just use Jacquard.Core — it includes AnthropicModel, OpenAICompatibleModel, and GeminiModel
```

## Minimal example

```csharp
using Jacquard.Core;
using Jacquard.Models.Bedrock;

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]);   // pass your tool classes here

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);

// Mark the class partial — the source generator fills in the rest at compile time
public partial class WeatherTools
{
    // Any public method with [Tool] becomes an agent tool automatically
    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";
}
```

The `[Tool]` attribute and `partial class` tell the Roslyn source generator — built into modern .NET — to generate the tool wiring at build time. You write the method; the framework handles the schema, dispatch, and result formatting.

### Other model providers

The example above uses Bedrock, but you can swap in any provider with one line — no other code changes needed:

```csharp
// Amazon Bedrock
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

All four providers support the same `IModel` interface — streaming, tool use, and structured output work identically regardless of which you choose.

## Key capabilities

- **Easy to learn, idiomatic to write** — any .NET developer can pick this up and ship a working agent in an afternoon. If you can write a C# method, you can write a tool. The advanced .NET features are present where they help and hidden where they don't.
- **Industry-standard vocabulary** — agent, tool, system prompt, session, hook. Reads natively to anyone coming from Strands Python, OpenAI, Anthropic, or LangChain. No proprietary terminology to translate.
- **Zero runtime reflection** — compile-time tool dispatch via Roslyn source generators. The `JACQUARD001` diagnostic catches tool misconfiguration at build time, not at first invocation.
- **NativeAOT-ready** — compile-time tool dispatch means no JIT tax on Lambda cold starts. Measured 93.3ms average across 60 cold starts on Graviton2 (512 MB–2048 MB), 88% under 100ms. Runs fast on small instances — the binary uses only ~52 MB at runtime. See the [AotLambda sample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/AotLambda).
- **Cloud-neutral core, deep integrations available** — four model providers (Bedrock, Anthropic, OpenAI-compatible, Gemini), open protocols (MCP, A2A), first-class AWS Bedrock and AgentCore support. Runs anywhere .NET runs.
- **Multi-agent in one package** — pipeline, parallel, graph orchestration, agent-as-tool, A2A protocol for cross-language interop.

## Where to go next

- **[Getting Started](./getting-started)** — install, configure, run your first agent
- **[Concepts: Agent & Event Loop](./concepts/agent-event-loop)** — understand the mental model
- **[Concepts: Model Providers](./concepts/model-providers)** — Bedrock, Anthropic, OpenAI, Gemini
- **[Concepts: AgentCore](./concepts/agentcore)** — Runtime, Memory, Code Interpreter, Browser, Gateway
- **[Tutorials](./tutorials/first-agent)** — step-by-step walkthroughs
- **[FAQ](./faq)** — common questions and troubleshooting

## About this project

Jacquard.NET is a ground-up C# implementation inspired by the [Strands Agents](https://strandsagents.com) design. The core concepts — model-driven event loop, tool system, hooks, multi-agent orchestration — are built natively in C# 13 for the .NET community. The A2A protocol implementation is interoperable across the Strands Python and TypeScript SDKs.

This project is independently maintained, community contributions welcome. Not affiliated with AWS or the Strands Agents project. Licensed under Apache 2.0.
