using System.Collections.Concurrent;
using StrandsAgents.Core;
using StrandsAgents.Extensions.DI;

var builder = WebApplication.CreateBuilder(args);

// Wire the Strands stack through DI — model, tools, session manager.
builder.Services
    .AddBedrockModel(
        region:  builder.Configuration["Bedrock:Region"]  ?? "us-east-1",
        modelId: builder.Configuration["Bedrock:ModelId"] ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0")
    .AddHttpRequestTool()
    .AddStrandsInMemorySessionManager();

var app = builder.Build();

// Per-session agents — each sessionId gets its own Agent with isolated conversation history.
var sessions = new ConcurrentDictionary<string, Agent>(StringComparer.Ordinal);

Agent GetOrCreate(string id, IModel model, IEnumerable<ITool> tools) =>
    sessions.GetOrAdd(id, _ => new Agent(model,
        systemPrompt: "You are a helpful assistant.",
        tools: tools.ToArray()));

// POST /chat  { "sessionId": "abc", "message": "hello" }  → SSE stream
app.MapPost("/chat", async (ChatRequest req, IModel model, IEnumerable<ITool> tools, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.SessionId) || string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "sessionId and message are required" });

    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    var agent = GetOrCreate(req.SessionId, model, tools);

    await foreach (var evt in agent.StreamAsync(req.Message, ct).ConfigureAwait(false))
    {
        var line = evt switch
        {
            TextDeltaEvent delta        => $"data: {delta.Delta}\n\n",
            AgentCompleteEvent complete => $"event: done\ndata: stop_reason={complete.Result.StopReason}\n\n",
            _                          => null,
        };
        if (line is null) continue;
        await ctx.Response.WriteAsync(line, ct).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    return Results.Empty;
});

app.MapDelete("/sessions/{id}", (string id) =>
    sessions.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound());

app.MapGet("/health", () => Results.Ok(new { status = "healthy", sessions = sessions.Count }));

app.Run();

internal record ChatRequest(string SessionId, string Message);
