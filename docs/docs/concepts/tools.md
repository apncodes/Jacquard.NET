---
sidebar_position: 2
---

# Tools & the `[Tool]` Attribute

## What it is

A **tool** is a C# method that the model can call. You declare it with the `[Tool]` attribute on a method in a `partial class`. The Roslyn source generator emits a compile-time `ITool` wrapper — no reflection, no runtime type discovery.

## Problem it solves

Without the source generator, you'd write a wrapper class for every tool method: implement `ITool`, define the JSON schema manually, write the deserialization code. The `[Tool]` attribute does all of this at compile time, and the schema is derived from the method signature and XML doc comments.

## How to use it

```csharp
public partial class WeatherTools
{
    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city) => $"Sunny, 22°C in {city}";

    [Tool("Converts temperature between Celsius and Fahrenheit")]
    public double ConvertTemp(double value, string from, string to)
    {
        return (from, to) switch
        {
            ("C", "F") => value * 9 / 5 + 32,
            ("F", "C") => (value - 32) * 5 / 9,
            _ => value
        };
    }
}
```

Pass the class to the agent via `toolProviders:`:

```csharp
var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a weather assistant.",
    toolProviders: [new WeatherTools()]);
```

## What the source generator emits

For each `[Tool]`-decorated method, the generator emits:
1. A `WeatherTools_GetWeather_Tool` class implementing `ITool` — contains the JSON schema and dispatch logic
2. A partial `WeatherTools : IToolProvider` implementation with a `GetTools()` method

You never reference these generated types directly. The `toolProviders:` parameter accepts `IToolProvider` instances, which is what `WeatherTools` becomes after generation.

## The `partial class` requirement

The class **must** be declared `partial`. The source generator emits its `IToolProvider` implementation as a second partial declaration in the same namespace. Without `partial`, the two declarations can't merge and the build fails.

If you forget `partial`, the compiler emits a `JACQUARD001` warning pointing to the class.

## Async tools

Tools can be async:

```csharp
public partial class SearchTools
{
    [Tool("Searches the web for current information")]
    public async Task<string> Search(string query)
    {
        // call an API, database, etc.
        var result = await _httpClient.GetStringAsync($"...");
        return result;
    }
}
```

## Parameter types

The source generator maps C# types to JSON schema types:

| C# type | JSON schema type |
|---|---|
| `string` | `"string"` |
| `int`, `long` | `"integer"` |
| `double`, `float` | `"number"` |
| `bool` | `"boolean"` |

For complex types, use `string` and parse JSON manually, or use the `[Tool]` description to guide the model.

## Tool parameter validation

Add `[ToolParameterValidation]` to constrain inputs before the tool is called:

```csharp
[Tool("Fetches content from a URL")]
public async Task<string> Fetch(
    [ToolParameterValidation(Required = true, MaxLength = 200, Pattern = "^https://")]
    string url)
{
    // url is guaranteed to be non-null, ≤200 chars, and start with https://
}
```

Validation runs in the `ToolRegistry` before your method is invoked. Invalid inputs return `ToolResult.Failure` without calling your code.
