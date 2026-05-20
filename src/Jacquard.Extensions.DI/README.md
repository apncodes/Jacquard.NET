# Jacquard.Extensions.DI

`Microsoft.Extensions.DependencyInjection` integration for [Jacquard.NET](https://github.com/apncodes/Jacquard.net).

```bash
dotnet add package Jacquard.Extensions.DI
```

```csharp
using Jacquard.Extensions.DI;

builder.Services
    .AddBedrockModel("us-east-1", "us.anthropic.claude-sonnet-4-5-v1:0")
    .AddFileReadTool("/var/data")
    .AddFileWriteTool("/var/data")
    .AddJacquardInMemorySessionManager()
    .AddJacquardAgent();

// Inject IAgent anywhere
app.MapPost("/ask", async (IAgent agent, AskRequest req) =>
    await agent.InvokeAsync(req.Prompt));
```

Supports Bedrock, Anthropic, Gemini, and OpenAI-compatible model providers. Works with ASP.NET Core,
Worker Services, Azure Functions, and AWS Lambda.
