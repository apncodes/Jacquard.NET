---
sidebar_position: 1
---

# Build Your First Agent

**Time:** ~15 minutes  
**What you'll build:** A multi-turn CLI agent with a custom tool, streaming output, and conversation memory.

## Prerequisites

- .NET 10 SDK
- AWS credentials with Bedrock access (Claude Haiku or similar)

## Step 1: Create the project

```bash
dotnet new console -n MyFirstAgent
cd MyFirstAgent
dotnet add package Jacquard.Core
dotnet add package Jacquard.Models.Bedrock
dotnet add package Jacquard.SourceGenerator
```

## Step 2: Define a tool

Create `WeatherTools.cs`:

```csharp
using Jacquard.Core;

namespace MyFirstAgent;

public partial class WeatherTools
{
    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city) =>
        $"Sunny, 22°C in {city}. Humidity: 45%. Wind: 12 km/h NW.";

    [Tool("Returns a 3-day weather forecast for a city")]
    public string GetForecast(string city) =>
        $"Forecast for {city}: Today sunny 22°C, Tomorrow cloudy 18°C, Day 3 rainy 15°C.";
}
```

Two things to notice:
- The class is `partial` — required for the source generator
- The `[Tool]` description is what the model sees when deciding whether to call the tool

## Step 3: Create the agent

Replace `Program.cs`:

```csharp
using Jacquard.Core;
using Jacquard.Models.Bedrock;
using MyFirstAgent;

var agent = new Agent(
    model: new BedrockModel(
        region: "us-east-1",
        modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0"),
    systemPrompt: """
        You are a helpful weather assistant.
        Use the weather tools to answer questions about current conditions and forecasts.
        Be concise and friendly.
        """,
    toolProviders: [new WeatherTools()]);

Console.WriteLine("Weather Assistant — type your question, 'exit' to quit");
Console.WriteLine();

while (true)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("You: ");
    Console.ResetColor();

    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Agent: ");
    Console.ResetColor();

    // Stream the response token by token
    await foreach (var evt in agent.StreamAsync(input))
    {
        if (evt is TextDeltaEvent delta)
            Console.Write(delta.Delta);
    }

    Console.WriteLine();
    Console.WriteLine();
}
```

## Step 4: Run it

```bash
dotnet run
```

Try these prompts:
- "What's the weather in London?"
- "Compare the weather in Tokyo and Sydney"
- "Should I bring an umbrella to Paris tomorrow?"

The agent will call the appropriate tools and synthesize a response. Because the agent maintains conversation history, you can follow up:
- "What about the day after?"
- "Which city has better weather?"

## What's happening under the hood

1. Your prompt is added to the conversation history
2. The model receives the history + tool schemas
3. The model decides to call `GetWeather` or `GetForecast` (or both)
4. The SDK executes the tools and adds results to the history
5. The model generates a final response using the tool results
6. The response is streamed back token by token

The `partial class WeatherTools` becomes an `IToolProvider` at compile time — the source generator emits the `GetTools()` implementation. No reflection happens at runtime.

## Next steps

- **[Tutorial: Production wiring with DI](./di-production)** — add dependency injection, sessions, and OpenTelemetry
- **[Concepts: Tools](../concepts/tools)** — learn more about the `[Tool]` attribute
- **[Concepts: Hooks](../concepts/hooks)** — add logging and human-in-the-loop approval
- **[Sample: CliAgent](https://github.com/apncodes/Jacquard.net/tree/main/samples/CliAgent)** — the full sample this tutorial is based on
