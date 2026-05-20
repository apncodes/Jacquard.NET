using Jacquard.Core;
using Jacquard.Models.Bedrock;
using Jacquard.MultiAgent;
using ResearchService;

// ResearchService — exposes a research agent over the A2A (Agent-to-Agent) protocol.
//
// This is one half of the DistributedAgents sample. The other half is WriterClient.
//
// Architecture:
//   POST /a2a  — A2A endpoint accepting { "prompt": "..." } JSON
//              — forwards to the local ResearchAgent
//              — responds with { "message": "...", "stopReason": "...", ... }
//
// SDK features shown:
//   • MapA2AEndpoint            — A2AEndpointExtensions maps an IAgent to a POST route
//   • [Tool] source generator   — WebSearchTool_Search_Tool (compile-time ITool)
//   • A2A protocol              — A2ARequest / A2AResponse transport types
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage (Terminal 1):
//   dotnet run --project samples/DistributedAgents/ResearchService
//   (listens on http://localhost:5100)
//
// Then start WriterClient in Terminal 2:
//   dotnet run --project samples/DistributedAgents/WriterClient

var builder = WebApplication.CreateBuilder(args);

// Bind to a fixed port so WriterClient always knows where to connect.
builder.WebHost.UseUrls("http://localhost:5100");

var model = new BedrockModel(
    region:  builder.Configuration["Bedrock:Region"]  ?? "us-east-1",
    modelId: builder.Configuration["Bedrock:ModelId"] ?? "us.anthropic.claude-haiku-4-5-20251001-v1:0");

var researchAgent = new Agent(
    model,
    systemPrompt: """
        You are a research specialist with access to a web search tool.
        When given a topic or question, use the Search tool to retrieve relevant information.
        Then synthesise the results into a well-structured research brief:
          • 2-3 sentences of context/background
          • 3-5 key findings as bullet points
          • 1 sentence on the significance or outlook
        Be factual, concise, and cite the source material where appropriate.
        """,
    tools: [new WebSearchTool_Search_Tool(new WebSearchTool())]);

var app = builder.Build();

// Expose the research agent at /a2a using the Strands A2A protocol.
app.MapA2AEndpoint("/a2a", researchAgent);

// Liveness probe for readiness checks.
app.MapGet("/health", () => Results.Ok(new { service = "ResearchService", status = "ready" }));

Console.WriteLine("ResearchService listening on http://localhost:5100");
Console.WriteLine("A2A endpoint: POST http://localhost:5100/a2a");
Console.WriteLine("Health check: GET  http://localhost:5100/health");
Console.WriteLine();
Console.WriteLine("Start WriterClient in a second terminal:");
Console.WriteLine("  dotnet run --project samples/DistributedAgents/WriterClient");
Console.WriteLine();

app.Run();
