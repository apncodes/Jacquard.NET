# StrandsAgents.SourceGenerator

Roslyn source generator for [Strands Agents .NET](https://github.com/apncodes/strands.net). Emits compile-time `ITool` wrappers from `[Tool]`-decorated methods — zero runtime reflection.

```bash
dotnet add package StrandsAgents.SourceGenerator
```

```csharp
using StrandsAgents.Core;

public class WeatherTool
{
    [Tool("Get current weather for a city")]
    public async Task<string> GetWeather(string city, CancellationToken ct = default)
        => $"Sunny, 22°C in {city}";
}

// Generated at compile time: WeatherTool_GetWeather_Tool
var agent = new Agent(model, tools: [new WeatherTool_GetWeather_Tool(new WeatherTool())]);
```

`CancellationToken` parameters are forwarded automatically and excluded from the JSON schema.
