using System.Text.Json;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.MultiAgent;
using SwarmResearchWeb;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5170");

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

// ── model + tools ────────────────────────────────────────────────────────────────

const string Region  = "us-east-1";
const string ModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

// ── POST /swarm — streams SwarmEvent records as SSE ──────────────────────────────
//
// Each SSE message has:
//   event: <event-type>
//   data:  <json-payload>
//
// Event types mirror the SwarmEvent hierarchy:
//   swarm_started, agent_started, agent_text_delta, agent_tool_call,
//   agent_tool_result, agent_completed, handoff, swarm_completed

app.MapPost("/swarm", async (HttpContext ctx) =>
{
    var body   = await JsonDocument.ParseAsync(ctx.Request.Body);
    var topic  = body.RootElement.TryGetProperty("topic", out var t)
        ? t.GetString() ?? "large language models"
        : "large language models";

    ctx.Response.ContentType  = "text/event-stream";
    ctx.Response.Headers.CacheControl  = "no-cache";
    ctx.Response.Headers.Connection    = "keep-alive";
    ctx.Response.Headers.Append("X-Accel-Buffering", "no"); // disable nginx buffering

    var ct = ctx.RequestAborted;

    static async Task Send(HttpResponse r, string type, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        await r.WriteAsync($"event: {type}\ndata: {json}\n\n", ct);
        await r.Body.FlushAsync(ct);
    }

    try
    {
        var model        = new BedrockModel(region: Region, modelId: ModelId);
        var researchTools = new ResearchTools();

        var researcher = new Agent(model,
            systemPrompt: """
                You are a research specialist. Gather verified facts and authoritative sources
                on the given topic using your available tools (SearchFacts and GetSources).
                Summarise your findings clearly. Do not write the article.
                When done, hand off to the analyst.
                """,
            tools:
            [
                new ResearchTools_SearchFacts_Tool(researchTools),
                new ResearchTools_GetSources_Tool(researchTools),
            ]);

        var analyst = new Agent(model,
            systemPrompt: """
                You are a content analyst. Organise the research into a structured outline:
                key themes, supporting evidence, narrative arc, and any gaps.
                Do not write the full article. Hand off to the writer when ready.
                """);

        var writer = new Agent(model,
            systemPrompt: """
                You are a technology journalist. Write a polished 400-600 word article
                for a general technical audience. Include a headline, introduction,
                body paragraphs, and conclusion. Hand off to the editor when done.
                """);

        var editor = new Agent(model,
            systemPrompt: """
                You are a senior editor. Use your ReviewDraft tool to get editorial critique,
                then apply improvements: fix factual gaps, improve balance, sharpen the prose.
                Produce the final publication-ready article. Terminate when satisfied.
                """,
            tools: [new ResearchTools_ReviewDraft_Tool(researchTools)]);

        var swarm = new SwarmOrchestrator(
        [
            new SwarmAgentNode("researcher", researcher, "Gathers verified facts and authoritative sources"),
            new SwarmAgentNode("analyst",   analyst,    "Organises research into a structured outline"),
            new SwarmAgentNode("writer",    writer,     "Writes the article draft"),
            new SwarmAgentNode("editor",    editor,     "Reviews and polishes the final article"),
        ],
        routingModel: model,
        entryPoint: "researcher",
        maxHandoffs: 10,
        maxIterations: 12,
        executionTimeout: TimeSpan.FromMinutes(10),
        nodeTimeout: TimeSpan.FromMinutes(3),
        repetitiveHandoffDetectionWindow: 6,
        repetitiveHandoffMinUniqueAgents: 3);

        await foreach (var evt in swarm.StreamAsync(
            $"Write a well-researched, balanced technology article about: {topic}", ct)
            .ConfigureAwait(false))
        {
            switch (evt)
            {
                case SwarmStartedEvent e:
                    await Send(ctx.Response, "swarm_started",
                        new { e.Task, e.EntryAgentId }, ct);
                    break;

                case AgentStartedEvent e:
                    await Send(ctx.Response, "agent_started",
                        new { e.AgentId, e.Description, e.HandoffMessage, e.Iteration }, ct);
                    break;

                case AgentTextDeltaEvent e:
                    await Send(ctx.Response, "agent_text_delta",
                        new { e.AgentId, e.Delta }, ct);
                    break;

                case AgentToolCallEvent e:
                    await Send(ctx.Response, "agent_tool_call",
                        new { e.AgentId, e.ToolName }, ct);
                    break;

                case AgentToolResultEvent e:
                    await Send(ctx.Response, "agent_tool_result",
                        new { e.AgentId, e.ToolCallId }, ct);
                    break;

                case AgentCompletedEvent e:
                    await Send(ctx.Response, "agent_completed",
                        new { e.AgentId, e.Result.Usage.InputTokens, e.Result.Usage.OutputTokens, Total = e.Result.Usage.Total }, ct);
                    break;

                case HandoffEvent e:
                    await Send(ctx.Response, "handoff",
                        new { e.FromAgentId, e.ToAgentId, e.Message }, ct);
                    break;

                case SwarmCompletedEvent e:
                    await Send(ctx.Response, "swarm_completed",
                        new
                        {
                            Status        = e.Status.ToString(),
                            e.FinalMessage,
                            TotalTokens   = e.TotalUsage.Total,
                            InputTokens   = e.TotalUsage.InputTokens,
                            OutputTokens  = e.TotalUsage.OutputTokens,
                            AgentPath     = e.NodeHistory.Select(n => n.AgentId).ToArray(),
                        }, ct);
                    break;
            }
        }
    }
    catch (OperationCanceledException) { /* client disconnected */ }
});

Console.WriteLine("Swarm Research Web  →  http://localhost:5170");
app.Run();
