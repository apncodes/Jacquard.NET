using Jacquard.Runtime.Extensions;
using Jacquard.Runtime.Hosting;
using Jacquard.Extensions.DI;
using Jacquard.Tools;

var builder = WebApplication.CreateBuilder(args);

// ── AGENT CONFIGURATION ───────────────────────────────────────────────────────────
// This block is IDENTICAL whether you run locally or on AgentCore Runtime.
// The agent has zero knowledge of where it will be hosted.
// Swap AddBedrockModel for AddAnthropicModel or AddOpenAICompatibleModel — nothing else changes.
// ─────────────────────────────────────────────────────────────────────────────────

var services = builder.Services
    .AddBedrockModel(
        region: "us-east-1",
        modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0")

    // Built-in tool — unchanged from any other Jacquard.NET agent
    .AddJacquardToolProvider<CalculatorTool>();

// Optional: AgentCore managed browser — enabled when AGENTCORE_BROWSER_ID is set.
// On AgentCore Runtime this env var is injected automatically.
// Locally: omit the env var and the agent runs without browser capability.
if (Environment.GetEnvironmentVariable("AGENTCORE_BROWSER_ID") is { Length: > 0 } browserId)
    services.AddAgentCoreBrowser(browserId, "us-east-1");

// Optional: AgentCore managed code execution sandbox — enabled when AGENTCORE_CODE_INTERPRETER_ID is set.
// On AgentCore Runtime this env var is injected automatically.
if (Environment.GetEnvironmentVariable("AGENTCORE_CODE_INTERPRETER_ID") is { Length: > 0 } codeInterpreterId)
    services.AddAgentCoreCodeInterpreter(codeInterpreterId, "us-east-1");

// Optional: AgentCore managed session storage — enabled when AGENTCORE_MEMORY_ID is set.
// On AgentCore Runtime this env var is injected automatically.
// Locally: omit the env var and the agent uses in-process session state.
if (Environment.GetEnvironmentVariable("AGENTCORE_MEMORY_ID") is { Length: > 0 } memoryId)
    services.AddAgentCoreSessionManager(memoryId, region: "us-east-1");

services.AddJacquardAgent();

// ── AGENTCORE HOSTING ─────────────────────────────────────────────────────────────
// ONE LINE makes this agent deployable to Amazon Bedrock AgentCore Runtime.
// Remove these two lines and you have a plain Jacquard.NET agent — the agent is unchanged.
// ─────────────────────────────────────────────────────────────────────────────────

var app = builder.Build();
app.MapAgentCoreEndpoints();  // registers POST /invocations + GET /health
app.UseAgentCorePort(8080);   // AgentCore Runtime routes traffic to port 8080
app.Run();
