---
sidebar_position: 2
---

# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dot.net)
- AWS credentials configured with Amazon Bedrock access
  - Run `aws configure` or set `AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY` environment variables
  - Enable model access in the [Bedrock console](https://console.aws.amazon.com/bedrock/home#/modelaccess)

## Install packages

```bash
dotnet add package StrandsAgents.Core
dotnet add package StrandsAgents.Models.Bedrock
dotnet add package StrandsAgents.SourceGenerator
```

:::tip SourceGenerator version
`StrandsAgents.SourceGenerator` 0.1.9+ is required for the `toolProviders:` pattern. If you're on an older version, upgrade: `dotnet add package StrandsAgents.SourceGenerator --version 0.1.9`
:::

## Your first agent

Create a new console app and add this code:

```csharp
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using MyApp;

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]);

var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);

namespace MyApp
{
    public partial class WeatherTools
    {
        [Tool("Returns the current weather for a city")]
        public string GetWeather(string city) => $"Sunny, 22°C in {city}";
    }
}
```

Run it:

```bash
dotnet run
```

The agent will call the `GetWeather` tool and return a response like:

> The weather in London is currently sunny with a temperature of 22°C.

## What just happened

1. You created a `WeatherTools` class with a `[Tool]`-decorated method
2. The Roslyn source generator emitted a compile-time `ITool` wrapper and `IToolProvider` implementation
3. The agent received your prompt, called the Bedrock model, which decided to use the `GetWeather` tool
4. The agent executed the tool, fed the result back to the model, and returned the final response

No reflection. No runtime type discovery. The tool schema was generated at compile time.

## Next steps

- **[Concepts: Agent & Event Loop](./concepts/agent-event-loop)** — understand how the loop works
- **[Concepts: Tools](./concepts/tools)** — learn about the `[Tool]` attribute and source generator
- **[Tutorial: Build your first agent](./tutorials/first-agent)** — a more detailed walkthrough
