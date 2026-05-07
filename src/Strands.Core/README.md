# Strands.Core

The [Strands Agents](https://strandsagents.com) framework for .NET — model-driven agentic AI built natively in C# 13.

```bash
dotnet add package Strands.Core
dotnet add package Strands.Models.Bedrock
dotnet add package Strands.SourceGenerator
```

Decorate any method with `[Tool]` — the source generator emits a compile-time `ITool` wrapper at build time:

```csharp
using Strands.Core;
using Strands.Core.Tools;
using Strands.Models.Bedrock;

// Define a tool
public class WeatherTools
{
    [Tool("get_weather", "Returns the current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";
}

// Wire up the agent — WeatherTools_GetWeather_Tool is generated at compile time
var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    tools: [new WeatherTools_GetWeather_Tool(new WeatherTools())]
);

// Single invocation
var result = await agent.InvokeAsync("What's the weather in London?");
Console.WriteLine(result.Message);

// Streaming
await foreach (var evt in agent.StreamAsync("Explain recursion"))
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Delta);
```

Full documentation and samples: [github.com/apncodes/strands.net](https://github.com/apncodes/strands.net)
