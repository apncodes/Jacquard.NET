using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Jacquard.Core;

namespace Jacquard.Runtime.Hosting;

/// <summary>
/// Handles <c>POST /invocations</c> — the core of the AgentCore hosting wrapper.
/// Resolves the agent from DI, invokes it, and returns the response.
///
/// Supports two modes:
/// <list type="bullet">
///   <item><description>Non-streaming: <c>Accept: application/json</c> — returns a single JSON response.</description></item>
///   <item><description>Streaming: <c>Accept: text/event-stream</c> — returns Server-Sent Events, one per token delta.</description></item>
/// </list>
///
/// The handler has zero knowledge of what model, tools, or hooks the agent uses.
/// It only calls <see cref="IAgent.InvokeAsync"/> or <see cref="IAgent.StreamAsync"/>.
/// </summary>
internal static class AgentCoreInvocationHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Handles an invocation request from AgentCore Runtime.</summary>
    internal static async Task HandleAsync(HttpContext ctx)
    {
        AgentCoreRequest? request;
        try
        {
            // AgentCore Runtime may omit Content-Type on the invocation request.
            // Force the content type to application/json so ReadFromJsonAsync works
            // regardless of what the runtime sends.
            ctx.Request.ContentType = "application/json";

            request = await ctx.Request.ReadFromJsonAsync<AgentCoreRequest>(_json, ctx.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(
                new { error = "Request body must be valid JSON." }, _json, ctx.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(
                new { error = "Missing required field: prompt." }, _json, ctx.RequestAborted)
                .ConfigureAwait(false);
            return;
        }

        var agent = ctx.RequestServices.GetRequiredService<IAgent>();
        var wantsStream = ctx.Request.Headers.Accept.ToString().Contains("text/event-stream",
            StringComparison.OrdinalIgnoreCase);

        if (wantsStream)
            await HandleStreamingAsync(ctx, agent, request).ConfigureAwait(false);
        else
            await HandleNonStreamingAsync(ctx, agent, request).ConfigureAwait(false);
    }

    private static async Task HandleNonStreamingAsync(
        HttpContext ctx, IAgent agent, AgentCoreRequest request)
    {
        var result = await agent.InvokeAsync(request.Prompt, ctx.RequestAborted)
            .ConfigureAwait(false);

        var response = new AgentCoreResponse
        {
            Result = result.Message,
            StopReason = result.StopReason.ToString(),
            Usage = new AgentCoreUsage
            {
                InputTokens = result.Usage.InputTokens,
                OutputTokens = result.Usage.OutputTokens,
            },
        };

        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(response, _json, ctx.RequestAborted)
            .ConfigureAwait(false);
    }

    private static async Task HandleStreamingAsync(
        HttpContext ctx, IAgent agent, AgentCoreRequest request)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers.Connection = "keep-alive";

        await foreach (var evt in agent.StreamAsync(request.Prompt, ctx.RequestAborted)
            .ConfigureAwait(false))
        {
            string? line = evt switch
            {
                TextDeltaEvent delta =>
                    $"data: {JsonSerializer.Serialize(new { text = delta.Delta }, _json)}\n\n",
                AgentCompleteEvent complete =>
                    $"data: {JsonSerializer.Serialize(new { stopReason = complete.Result.StopReason.ToString() }, _json)}\n\ndata: [DONE]\n\n",
                _ => null,
            };

            if (line is null) continue;

            await ctx.Response.WriteAsync(line, ctx.RequestAborted).ConfigureAwait(false);
            await ctx.Response.Body.FlushAsync(ctx.RequestAborted).ConfigureAwait(false);
        }
    }
}
