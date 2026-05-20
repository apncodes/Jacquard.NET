using Jacquard.Core;
using Jacquard.Extensions.DI;
using CustomerServiceApi;

// CustomerServiceApi — demonstrates Strands SDK in an ASP.NET Core minimal API.
//
// Architecture:
//   POST /sessions          → create a new chat session (returns session ID)
//   POST /sessions/{id}/chat → stream an SSE response from the customer-service agent
//   DELETE /sessions/{id}   → delete a session and its history
//   GET  /health            → liveness probe
//
// SDK features shown:
//   • AddBedrockModel / AddStrandsAgent  — DI registration
//   • Source-generated tools             — OrderStatusTool_GetStatus_Tool, KnowledgeBaseTool_Search_Tool
//   • ChatSessionStore                   — per-session Agent with typed system prompt + hooks
//   • FileSessionManager                 — disk-backed conversation persistence
//   • IAsyncEnumerable<StreamEvent>     — SSE streaming via StreamAsync
//   • HookRegistry                       — per-session logging hooks (BeforeToolCall, AfterToolCall, AfterModelCall)
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage:
//   dotnet run

var builder = WebApplication.CreateBuilder(args);

// ── model ─────────────────────────────────────────────────────────────────────

builder.Services.AddBedrockModel(
    region: builder.Configuration["Bedrock:Region"] ?? "us-east-1",
    modelId: builder.Configuration["Bedrock:ModelId"] ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// ── session persistence ────────────────────────────────────────────────────────

var sessionsDir = Path.Combine(
    builder.Environment.ContentRootPath, "sessions");

builder.Services.AddSingleton<ISessionManager>(_ => new FileSessionManager(sessionsDir));

// ── tools — registered as factory lambdas (generated classes need wrapping type) ──

builder.Services.AddTransient<ITool>(_ =>
    new OrderStatusTool_GetStatus_Tool(new OrderStatusTool()));

builder.Services.AddTransient<ITool>(_ =>
    new KnowledgeBaseTool_Search_Tool(new KnowledgeBaseTool()));

// ── chat session store ─────────────────────────────────────────────────────────

builder.Services.AddSingleton<ChatSessionStore>();

// ── build ──────────────────────────────────────────────────────────────────────

var app = builder.Build();

// ── endpoints ──────────────────────────────────────────────────────────────────

// Create a new session and return its ID.
app.MapPost("/sessions", () =>
{
    var id = Guid.NewGuid().ToString("N");
    return Results.Ok(new { sessionId = id });
});

// Stream a chat response as Server-Sent Events.
app.MapPost("/sessions/{id}/chat", async (
    string id,
    ChatRequest request,
    ChatSessionStore store,
    HttpContext ctx,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest(new { error = "message is required" });

    var agent = store.GetOrCreate(id);

    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    await foreach (var evt in agent.StreamAsync(request.Message, ct).ConfigureAwait(false))
    {
        string? data = evt switch
        {
            TextDeltaEvent delta => delta.Delta,
            _ => null,
        };

        if (data is null)
            continue;

        // SSE format: "data: <payload>\n\n"
        await ctx.Response.WriteAsync($"data: {data}\n\n", ct).ConfigureAwait(false);
        await ctx.Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    // Signal end-of-stream to the client.
    await ctx.Response.WriteAsync("data: [DONE]\n\n", ct).ConfigureAwait(false);

    return Results.Empty;
});

// Delete a session (removes in-memory agent; disk session files are kept for audit).
app.MapDelete("/sessions/{id}", (string id, ChatSessionStore store) =>
    store.Remove(id) ? Results.NoContent() : Results.NotFound());

// Liveness probe.
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();

// ── request model ──────────────────────────────────────────────────────────────

/// <summary>Request body for the chat endpoint.</summary>
internal record ChatRequest(string Message);
