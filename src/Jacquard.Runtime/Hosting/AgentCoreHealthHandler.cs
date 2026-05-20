using Microsoft.AspNetCore.Http;

namespace Jacquard.Runtime.Hosting;

/// <summary>
/// Handles <c>GET /health</c> — required by AgentCore Runtime before routing traffic.
/// Must return 200 OK; without it, no invocations will be received.
/// </summary>
internal static class AgentCoreHealthHandler
{
    /// <summary>Returns a 200 OK response indicating the agent is healthy.</summary>
    internal static Task HandleAsync(HttpContext ctx)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsJsonAsync(new
        {
            status = "healthy",
            framework = "Strands.NET",
            timestamp = DateTimeOffset.UtcNow,
        });
    }
}
