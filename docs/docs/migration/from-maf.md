---
sidebar_position: 1
---

# Migrating from Microsoft Agent Framework

If you're coming from Microsoft Agent Framework (MAF), most concepts map directly to Strands Agents .NET equivalents.

## Concept mapping

| MAF | Strands Agents .NET | Notes |
|---|---|---|
| `AIAgent` | `Agent` | Same role — the central orchestration class |
| `AIFunctionFactory.Create(method)` | `[Tool]` attribute on a `partial class` | Compile-time vs runtime tool registration |
| `ChatOptions.Tools` | `toolProviders:` constructor parameter | Pass your class directly, not wrapper instances |
| `AgentThread` | `ISessionManager` | Inject via DI or pass `sessionId` to `InvokeAsync` |
| Hook middleware | `agent.Hooks.Register<TEvent>()` | Type-safe, compiler-checked event registration |
| `WorkflowBuilder` | `GraphBuilder` | Conditional routing; see also `PipelineOrchestrator` |
| `IChatClient` | `IModel` | The model abstraction interface |
| `AIFunctionFactory` | `StrandsAgents.SourceGenerator` | Compile-time vs runtime schema generation |

## Tool registration

**MAF:**
```csharp
[Description("Get weather for a city")]
static string GetWeather([Description("City name")] string city) => $"Sunny in {city}";

var agent = openAIClient
    .GetChatClient("gpt-4o")
    .CreateAIAgent(
        instructions: "You are a helpful assistant.",
        tools: [AIFunctionFactory.Create(GetWeather)]);
```

**Strands Agents .NET:**
```csharp
public partial class WeatherTools
{
    [Tool("Get weather for a city")]
    public string GetWeather(string city) => $"Sunny in {city}";
}

var agent = new Agent(
    model: new BedrockModel("us-east-1"),
    systemPrompt: "You are a helpful assistant.",
    toolProviders: [new WeatherTools()]);
```

The key difference: MAF uses `AIFunctionFactory.Create()` at runtime to build tool schemas via reflection. Strands Agents .NET uses the Roslyn source generator to emit tool schemas at compile time — zero runtime reflection, AOT-safe.

## Workflow durability

MAF ships `Microsoft.Agents.AI.DurableTask` for built-in workflow durability. Strands Agents .NET defers durability to AWS Step Functions — each pipeline stage is a separate Lambda function, and Step Functions manages checkpointing and retry.

See the [DurableWorkflow sample](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/DurableWorkflow) for the recommended pattern.

## DI registration

**MAF:**
```csharp
services.AddChatClient(openAIClient.GetChatClient("gpt-4o"))
    .UseFunctionInvocation();
```

**Strands Agents .NET:**
```csharp
services
    .AddBedrockModel("us-east-1")
    .AddStrandsToolProvider<WeatherTools>()
    .AddStrandsAgent();
```
