---
sidebar_position: 2
---

# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dot.net)
- AWS credentials configured with Amazon Bedrock access
  - Run `aws configure` or set `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` environment variables
  - Enable model access in the [Bedrock console](https://console.aws.amazon.com/bedrock/home#/modelaccess)

:::tip Don't have AWS/Bedrock?
You can use any supported model provider. Replace `BedrockModel` with `AnthropicModel`, `OpenAICompatibleModel`, or `GeminiModel`:

```csharp
// Anthropic direct API
var model = new AnthropicModel(apiKey: "sk-ant-...", modelId: "claude-sonnet-4-5");

// OpenAI / Azure OpenAI / Ollama
var model = new OpenAICompatibleModel(
    baseUrl: "https://api.openai.com/v1",
    apiKey: "sk-...",
    modelId: "gpt-4o");

// Google Gemini
var model = new GeminiModel(apiKey: "...", modelId: "gemini-2.5-flash");
```
:::

## Install packages

```bash
dotnet add package Jacquard.Core
dotnet add package Jacquard.Models.Bedrock
dotnet add package Jacquard.Tools
dotnet add package Jacquard.SourceGenerator
```

## Your first agent

Create a new console app and add this code:

```csharp
using Jacquard.Core;
using Jacquard.Models.Bedrock;
using Jacquard.Tools;
using QuickTools;

var model = new BedrockModel(
    region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// Three tool providers: one built-in, two custom (defined below)
var agent = new Agent(
    model,
    toolProviders: [new CalculatorTool(), new CurrentTimeTool(), new LetterCounterTool()]);

var message = """
    I have 3 requests:
    1. What is the time right now?
    2. Calculate 3111696 / 74088
    3. Tell me how many letter R's are in the word "strawberry" 🍓
    """;

Console.Write("Agent: ");
await foreach (var evt in agent.StreamAsync(message))
{
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Delta);
}

// ── Custom tools ──────────────────────────────────────────────────────────────
// Decorate a method with [Tool] inside a partial class and the source generator
// emits a fully-typed ITool wrapper with a JSON schema at compile time.
// XML doc comments become the descriptions the model sees when choosing a tool.
// ─────────────────────────────────────────────────────────────────────────────

namespace QuickTools
{
    public sealed partial class LetterCounterTool
    {
        /// <summary>Counts occurrences of a specific letter in a word.</summary>
        /// <param name="word">The input word to search in.</param>
        /// <param name="letter">The single character to count.</param>
        [Tool("Count occurrences of a specific letter in a word.")]
        public int CountLetter(string word, string letter)
        {
            if (letter.Length != 1)
                throw new ArgumentException("Must be a single character.", nameof(letter));
            return word.ToLowerInvariant().Count(c => c == char.ToLowerInvariant(letter[0]));
        }
    }

    public sealed partial class CurrentTimeTool
    {
        /// <summary>Returns the current UTC date and time.</summary>
        [Tool("Returns the current UTC date and time.")]
        public string GetCurrentTime() =>
            DateTimeOffset.UtcNow.ToString("dddd, MMMM d, yyyy HH:mm:ss 'UTC'");
    }
}
```

Run it:

```bash
dotnet run
```

The agent calls all three tools and streams back a response covering the current time, the calculation result, and the letter count.

## What just happened

1. `CalculatorTool` is a built-in tool from `Jacquard.Tools`
2. `CurrentTimeTool` and `LetterCounterTool` are custom tools — each is a `partial class` with a `[Tool]`-decorated method
3. The Roslyn source generator emitted compile-time `ITool` wrappers and `IToolProvider` implementations for all three — no runtime reflection
4. The agent received the prompt, the model decided which tools to call and in what order, the SDK executed them, and the results were fed back to the model to produce the final streamed response

:::tip Custom tool placement
Type declarations must come after top-level statements in the same file. Use a block-body namespace (not file-scoped `namespace MyApp;`) when mixing with top-level statements. For larger projects, move tool classes to a separate file with a file-scoped namespace.
:::

:::tip toolProviders vs tools
Use `toolProviders:` when passing your `[Tool]`-decorated classes — the common case. Use `tools:` when you have pre-built `ITool` instances, such as from `agent.AsTool()` or `AgentCoreGatewayToolProvider`.
:::

## Next steps

- **[Concepts: Agent & Event Loop](./concepts/agent-event-loop)** — understand how the loop works
- **[Concepts: Tools](./concepts/tools)** — learn about the `[Tool]` attribute and source generator
- **[Tutorial: Build your first agent](./tutorials/first-agent)** — a more detailed walkthrough
- **[QuickstartSample](https://github.com/apncodes/Jacquard.NET/tree/main/samples/QuickstartSample)** — the full runnable version of the code on this page
