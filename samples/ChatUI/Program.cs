using System.Collections.Concurrent;
using System.Text.Json;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using ChatUI;

// ChatUI — a browser-based chat UI backed by a Strands streaming agent.
//
// Architecture:
//   GET  /           → serves wwwroot/index.html (the chat frontend)
//   POST /chat       → SSE stream; creates or resumes a per-session Agent
//   DELETE /sessions/{id} → removes an in-memory session
//   GET  /health     → liveness probe
//
// SDK features shown:
//   • [Tool] source generator  — AssistantTools_GetWeather_Tool, AssistantTools_GetCurrentTime_Tool
//   • StreamAsync              — per-event SSE delivery (delta / tool / done)
//   • Per-session Agent        — each browser tab gets its own Agent with isolated conversation
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage:
//   dotnet run
//   Then open http://localhost:5000 in your browser.

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5000");

var model = new BedrockModel(
    region:  builder.Configuration["Bedrock:Region"]  ?? "us-east-1",
    modelId: builder.Configuration["Bedrock:ModelId"] ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// Shared tool instances — stateless, safe to reuse across sessions.
var tools = new AssistantTools();
ITool[] toolInstances =
[
    new AssistantTools_GetWeather_Tool(tools),
    new AssistantTools_GetCurrentTime_Tool(tools),
];

// In-memory session store — each browser session gets its own Agent with conversation history.
var sessions = new ConcurrentDictionary<string, Agent>(StringComparer.Ordinal);

Agent GetOrCreate(string sessionId) => sessions.GetOrAdd(sessionId, _ => new Agent(
    model,
    systemPrompt: """
        You are a helpful, friendly assistant.
        You have two tools:
          • GetWeather  — look up current weather for a city
          • GetCurrentTime — look up the current local time in a city or timezone
        Always use a tool when the user asks about weather or time — never guess.
        For other questions, answer directly and concisely.
        Keep responses brief and conversational.
        """,
    tools: toolInstances));

var app = builder.Build();
app.UseDefaultFiles();   // serves index.html for GET /
app.UseStaticFiles();

// ── SSE chat endpoint ──────────────────────────────────────────────────────────

app.MapPost("/chat", async (ChatRequest req, HttpContext ctx, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Message))
        return Results.BadRequest(new { error = "message is required" });

    if (string.IsNullOrWhiteSpace(req.SessionId))
        return Results.BadRequest(new { error = "sessionId is required" });

    var agent = GetOrCreate(req.SessionId);

    ctx.Response.ContentType  = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection   = "keep-alive";

    // Helper: write one SSE event and flush.
    async Task Emit(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    await foreach (var evt in agent.StreamAsync(req.Message, ct).ConfigureAwait(false))
    {
        switch (evt)
        {
            case TextDeltaEvent delta:
                await Emit(new { t = "delta", text = delta.Delta }).ConfigureAwait(false);
                break;

            case ToolCallStartEvent ts:
                await Emit(new { t = "tool", name = ts.ToolName }).ConfigureAwait(false);
                break;

            case AgentCompleteEvent complete:
                await Emit(new
                {
                    t  = "done",
                    sr = complete.Result.StopReason.ToString(),
                    @in = complete.Result.Usage.InputTokens,
                    @out = complete.Result.Usage.OutputTokens,
                }).ConfigureAwait(false);
                break;
        }
    }

    return Results.Empty;
});

// ── session management ─────────────────────────────────────────────────────────

app.MapDelete("/sessions/{id}", (string id) =>
    sessions.TryRemove(id, out _) ? Results.NoContent() : Results.NotFound());

app.MapGet("/health", () => Results.Ok(new { status = "healthy", sessions = sessions.Count }));

app.Run();

// ── request model ──────────────────────────────────────────────────────────────

/// <summary>Chat request body.</summary>
internal record ChatRequest(string SessionId, string Message);
