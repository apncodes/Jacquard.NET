# Strands.Extensions.DI

`Microsoft.Extensions.DependencyInjection` integration for [Strands.NET](https://github.com/apncodes/strands.net).

```bash
dotnet add package Strands.Extensions.DI
```

```csharp
// Program.cs — wire everything in one chain
builder.Services
    .AddBedrockModel("us-east-1", "us.anthropic.claude-sonnet-4-5-v1:0")
    .AddFileReadTool("/var/data")
    .AddFileWriteTool("/var/data")
    .AddStrandsInMemorySessionManager()
    .AddStrandsAgent();

// Inject IAgent anywhere
app.MapPost("/ask", async (IAgent agent, AskRequest req) =>
    await agent.InvokeAsync(req.Prompt));
```

Supports Bedrock, Anthropic, and OpenAI-compatible model providers. Works with ASP.NET Core, Worker Services, Azure Functions, and AWS Lambda.
