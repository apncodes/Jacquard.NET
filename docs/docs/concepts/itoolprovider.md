---
sidebar_position: 3
---

# IToolProvider Pattern

## What it is

`IToolProvider` is an interface with a single method: `IEnumerable<ITool> GetTools()`. When you declare a `partial class` with `[Tool]` methods, the source generator emits a `IToolProvider` implementation automatically. You pass instances of your class directly to the `Agent` constructor via `toolProviders:`.

## Problem it solves

Before `IToolProvider`, you had to reference generated wrapper type names directly:

```csharp
// Old pattern — requires knowing generated type names
var agent = new Agent(model, tools: [
    new WeatherTools_GetWeather_Tool(new WeatherTools()),
    new WeatherTools_ConvertTemp_Tool(new WeatherTools())
]);
```

With `IToolProvider`, you pass the class itself:

```csharp
// New pattern — no generated type names in user code
var agent = new Agent(model, toolProviders: [new WeatherTools()]);
```

The `toolProviders:` parameter accepts any `IToolProvider`. The agent calls `GetTools()` on each provider and registers all returned tools.

## `tools:` vs `toolProviders:`

| Parameter | Use when |
|---|---|
| `toolProviders:` | Passing your `[Tool]`-decorated classes (the common case) |
| `tools:` | Passing pre-built `ITool` instances — e.g., from `agent.AsTool()`, `AgentCoreGatewayToolProvider`, or `McpToolProvider` |

Both can be used simultaneously:

```csharp
var agent = new Agent(
    model: model,
    toolProviders: [new WeatherTools(), new SearchTools()],
    tools: [gatewayProvider.GetTools()..., researchAgent.AsTool("researcher", "...")]
);
```

## DI registration

With `StrandsAgents.Extensions.DI`, register tool providers as services:

```csharp
builder.Services
    .AddBedrockModel("us-east-1")
    .AddStrandsToolProvider<WeatherTools>()   // registers WeatherTools as IToolProvider
    .AddStrandsToolProvider<SearchTools>()
    .AddStrandsAgent();
```

`AddStrandsAgent()` resolves all registered `IToolProvider` instances and passes them to the `Agent` constructor automatically.

## STRAND001 diagnostic

If you add `[Tool]` methods to a class that isn't `partial`, the source generator emits a `STRAND001` warning:

```
warning STRAND001: Class 'WeatherTools' has [Tool] methods but is not declared partial.
Declare it partial to enable the IToolProvider pattern.
```

The per-method wrapper classes are still emitted so existing code keeps compiling, but the `IToolProvider` implementation is not generated. Add `partial` to the class declaration to resolve the warning.
