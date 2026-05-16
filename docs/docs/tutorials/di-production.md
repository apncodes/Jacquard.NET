---
sidebar_position: 2
---

# Production Wiring with DI

**Time:** ~20 minutes  
**What you'll build:** An ASP.NET Core API with a Strands agent wired via dependency injection, file-based session persistence, and OpenTelemetry tracing.

## Prerequisites

- .NET 10 SDK
- AWS credentials with Bedrock access
- Completed [Build Your First Agent](./first-agent) tutorial (recommended)

## Step 1: Create the project

```bash
dotnet new webapi -n WeatherApi
cd WeatherApi
dotnet add package StrandsAgents.Core
dotnet add package StrandsAgents.Models.Bedrock
dotnet add package StrandsAgents.Extensions.DI
dotnet add package StrandsAgents.SourceGenerator
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.Console
```

## Step 2: Define the tool

Create `Tools/WeatherTools.cs`:

```csharp
using StrandsAgents.Core;

namespace WeatherApi.Tools;

public partial class WeatherTools
{
    private readonly ILogger<WeatherTools> _logger;

    public WeatherTools(ILogger<WeatherTools> logger)
    {
        _logger = logger;
    }

    [Tool("Returns the current weather for a city")]
    public string GetWeather(string city)
    {
        _logger.LogInformation("Getting weather for {City}", city);
        return $"Sunny, 22°C in {city}";
    }
}
```

Note: tool classes can accept constructor-injected dependencies when registered via DI.

## Step 3: Wire up DI

Replace `Program.cs`:

```csharp
using StrandsAgents.Extensions.DI;
using WeatherApi.Tools;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ── Agent wiring ──────────────────────────────────────────────────────────────
builder.Services
    .AddBedrockModel(
        region: "us-east-1",
        modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0")
    .AddStrandsToolProvider<WeatherTools>()
    .AddStrandsFileSessionManager(
        basePath: Path.Combine(Path.GetTempPath(), "weather-api-sessions"))
    .AddStrandsAgent(systemPrompt: "You are a helpful weather assistant.");

// ── OpenTelemetry ─────────────────────────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("StrandsAgents.Agent")   // the SDK's ActivitySource
        .AddConsoleExporter());             // swap for OTLP in production

var app = builder.Build();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapPost("/chat", async (ChatRequest req, IAgent agent) =>
{
    var result = await agent.InvokeAsync(req.Message, sessionId: req.SessionId);
    return new ChatResponse(result.Message, req.SessionId ?? Guid.NewGuid().ToString());
});

app.Run();

record ChatRequest(string Message, string? SessionId);
record ChatResponse(string Message, string SessionId);
```

## Step 4: Run and test

```bash
dotnet run
```

Send a request:

```bash
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "What is the weather in London?", "sessionId": "user-123"}'
```

Send a follow-up using the same session ID:

```bash
curl -X POST http://localhost:5000/chat \
  -H "Content-Type: application/json" \
  -d '{"message": "What about Paris?", "sessionId": "user-123"}'
```

The agent remembers the conversation context because both requests use the same `sessionId`. The session is persisted to disk, so it survives process restarts.

## What DI gives you

**Lifetime management:** `AddStrandsAgent()` registers `IAgent` as a scoped service. Each HTTP request gets its own agent instance, but the session manager is singleton — sessions persist across requests.

**Tool injection:** `AddStrandsToolProvider<WeatherTools>()` registers `WeatherTools` as a transient `IToolProvider`. The DI container resolves its `ILogger<WeatherTools>` dependency automatically.

**Testability:** Swap `BedrockModel` for a mock `IModel` in tests without changing any agent code.

## OpenTelemetry traces

The SDK emits traces via an `ActivitySource` named `"StrandsAgents.Agent"`. Each agent invocation creates a root span with child spans for each model call and tool execution. In the console output you'll see:

```
Activity.DisplayName: Agent.Invoke
Activity.Duration:    00:00:02.4567890
  Activity.DisplayName: Model.Call
  Activity.Duration:    00:00:02.1234567
  Activity.DisplayName: Tool.Execute/GetWeather
  Activity.Duration:    00:00:00.0001234
```

Replace `AddConsoleExporter()` with `AddOtlpExporter()` to send traces to Jaeger, Zipkin, AWS X-Ray, or any OTLP-compatible backend.

## Next steps

- **[Tutorial: Deploy to Lambda with AOT](./aot-lambda)** — take this agent to production on AWS Lambda
- **[Sample: DiAgent](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/DiAgent)** — the full DI sample
- **[Sample: AspNetAgent](https://github.com/apncodes/StrandsAgents.net/tree/main/samples/AspNetAgent)** — SSE streaming with session continuity
