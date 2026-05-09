# Strands.SourceGenerator

Roslyn source generator for [Strands.NET](https://github.com/apncodes/strands.net). Emits compile-time `ITool` wrappers from `[Tool]`-decorated methods — zero runtime reflection.

```bash
dotnet add package Strands.SourceGenerator
```

```csharp
public class WeatherTool
{
    [Tool("Get current weather for a city")]
    public async Task<string> GetWeather(string city, CancellationToken ct = default)
        => $"Sunny, 22°C in {city}";
}

// Generated at compile time: WeatherTool_GetWeather_Tool
// JSON schema is a string literal — no reflection at runtime
var agent = new Agent(model, tools: [new WeatherTool_GetWeather_Tool(new WeatherTool())]);
```

The generator handles `CancellationToken` parameters automatically — they are forwarded from `InvokeAsync`'s `ct` and excluded from the JSON schema.
