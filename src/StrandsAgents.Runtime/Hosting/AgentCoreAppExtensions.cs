using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace StrandsAgents.Runtime.Hosting;

/// <summary>
/// Extension methods to make any Strands.NET agent deployable to
/// Amazon Bedrock AgentCore Runtime.
///
/// <para>
/// Python equivalent:
/// <code>
/// app = BedrockAgentCoreApp()
/// @app.entrypoint
/// def invoke(payload): ...
/// app.run()
/// </code>
/// </para>
///
/// <para>
/// .NET equivalent:
/// <code>
/// app.MapAgentCoreEndpoints();
/// app.UseAgentCorePort(8080);
/// app.Run();
/// </code>
/// </para>
/// </summary>
public static class AgentCoreAppExtensions
{
    /// <summary>
    /// Maps the two HTTP endpoints required by AgentCore Runtime:
    /// <list type="bullet">
    ///   <item><description><c>POST /invocations</c> — receives prompts, returns agent responses (streaming or non-streaming).</description></item>
    ///   <item><description><c>GET /health</c> — health check polled by AgentCore Runtime before routing traffic.</description></item>
    /// </list>
    ///
    /// <para>The agent is resolved from DI and is completely unchanged — this method only adds HTTP routing.</para>
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="invocationsPath">Path for the invocations endpoint. Default: <c>/invocations</c>.</param>
    /// <param name="healthPath">Path for the health endpoint. Default: <c>/health</c>.</param>
    /// <returns>The same <see cref="WebApplication"/> for chaining.</returns>
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    ///
    /// builder.Services
    ///     .AddBedrockModel("us-east-1")
    ///     .AddStrandsAgent("You are a helpful assistant.");
    ///
    /// var app = builder.Build();
    /// app.MapAgentCoreEndpoints();  // one line — agent is now AgentCore-deployable
    /// app.UseAgentCorePort(8080);
    /// app.Run();
    /// </code>
    /// </example>
    public static WebApplication MapAgentCoreEndpoints(
        this WebApplication app,
        string invocationsPath = "/invocations",
        string healthPath = "/health")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(healthPath);

        app.MapPost(invocationsPath, AgentCoreInvocationHandler.HandleAsync);
        app.MapGet(healthPath, AgentCoreHealthHandler.HandleAsync);

        return app;
    }

    /// <summary>
    /// Configures the application to listen on port 8080 on all interfaces.
    /// AgentCore Runtime expects agents to listen on port 8080.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="port">The port to listen on. Default: <c>8080</c>.</param>
    /// <returns>The same <see cref="WebApplication"/> for chaining.</returns>
    public static WebApplication UseAgentCorePort(
        this WebApplication app,
        int port = 8080)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.Urls.Add($"http://0.0.0.0:{port}");
        return app;
    }
}
