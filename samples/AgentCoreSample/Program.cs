using StrandsAgents.Runtime.Extensions;
using StrandsAgents.Runtime.Hosting;
using StrandsAgents.Extensions.DI;
using StrandsAgents.Tools;

var builder = WebApplication.CreateBuilder(args);

// ── AGENT CONFIGURATION ───────────────────────────────────────────────────────────
// This block is IDENTICAL whether you run locally or on AgentCore Runtime.
// The agent has zero knowledge of where it will be hosted.
// Swap UseBedrockModel for UseAnthropicModel or UseOpenAICompatibleModel — nothing else changes.
// ─────────────────────────────────────────────────────────────────────────────────

builder.Services
    .AddBedrockModel(
        region: "us-east-1",
        modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0")

    // Built-in tool — unchanged from any other Strands.NET agent
    .AddStrandsTool<CalculatorTool_Calculate_Tool>()

    // Optional: AgentCore managed browser — for JS-rendered pages
    // Comment this out and the agent still runs identically everywhere else
    .AddAgentCoreBrowser("us-east-1")

    // Optional: AgentCore managed code execution sandbox
    .AddAgentCoreCodeInterpreter("us-east-1")

    // Optional: AgentCore managed session storage
    // Falls back to in-process state when AGENTCORE_MEMORY_ID is not set
    .AddAgentCoreSessionManager(
        memoryId: Environment.GetEnvironmentVariable("AGENTCORE_MEMORY_ID") ?? "local-dev",
        region: "us-east-1")

    .AddStrandsAgent();

// ── AGENTCORE HOSTING ─────────────────────────────────────────────────────────────
// Python: @app.entrypoint + app.run()
// .NET:   app.MapAgentCoreEndpoints() + app.Run()
//
// ONE LINE makes this agent deployable to Amazon Bedrock AgentCore Runtime.
// Remove these two lines and you have a plain Strands.NET agent — the agent is unchanged.
// ─────────────────────────────────────────────────────────────────────────────────

var app = builder.Build();
app.MapAgentCoreEndpoints();  // registers POST /invocations + GET /health
app.UseAgentCorePort(8080);   // AgentCore Runtime routes traffic to port 8080
app.Run();
