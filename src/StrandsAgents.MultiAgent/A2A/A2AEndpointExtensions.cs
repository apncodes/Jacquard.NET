using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StrandsAgents.Core;

namespace StrandsAgents.MultiAgent;

/// <summary>ASP.NET Core minimal API extension to expose an <see cref="IAgent"/> over A2A.</summary>
public static class A2AEndpointExtensions
{
    /// <summary>
    /// Maps a POST endpoint at <paramref name="pattern"/> that accepts A2A requests
    /// and forwards them to <paramref name="agent"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapA2AEndpoint(
        this IEndpointRouteBuilder app,
        string pattern,
        IAgent agent)
    {
        app.MapPost(pattern, async (A2ARequest request, CancellationToken ct) =>
        {
            var result = await agent.InvokeAsync(request.Prompt, ct);
            return Results.Ok(new A2AResponse(
                result.Message,
                result.StopReason.ToString(),
                result.Usage.InputTokens,
                result.Usage.OutputTokens));
        });
        return app;
    }
}
