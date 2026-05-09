using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.MultiAgent;

// WriterClient — calls a remote ResearchService over A2A and uses it as a tool
// inside a local Writer agent.
//
// This is the second half of the DistributedAgents sample.
//
// Architecture:
//   A2AAgent("http://localhost:5100/a2a")
//       ↓  wrapped as AgentTool("researcher", ...)
//   WriterAgent  — calls the researcher tool, then writes a polished article
//
// SDK features shown:
//   • A2AAgent     — IAgent that forwards InvokeAsync to a remote HTTP endpoint
//   • AgentTool    — wraps any IAgent as an ITool usable by a parent agent
//   • IDisposable  — A2AAgent disposes its internal HttpClient when owns it
//
// Prerequisites:
//   • AWS credentials configured for Bedrock
//   • ResearchService running: dotnet run --project samples/DistributedAgents/ResearchService
//
// Usage (Terminal 2, after starting ResearchService in Terminal 1):
//   dotnet run --project samples/DistributedAgents/WriterClient
//   dotnet run --project samples/DistributedAgents/WriterClient -- "quantum computing"

const string Region       = "us-east-1";
const string ModelId      = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
const string ResearchUrl  = "http://localhost:5100/a2a";

var topic = args.Length > 0
    ? string.Join(" ", args)
    : "the future of renewable energy";

Console.WriteLine(new string('═', 70));
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"  Distributed Agents — WriterClient");
Console.WriteLine($"  Topic: {topic}");
Console.ResetColor();
Console.WriteLine(new string('═', 70));
Console.WriteLine();

// ── remote research agent (A2A) ────────────────────────────────────────────────

// A2AAgent owns its HttpClient (no external client passed) — Dispose releases it.
using var remoteResearcher = new A2AAgent(new Uri(ResearchUrl));

// Wrap the remote agent as a tool so the local WriterAgent can call it by name.
// AgentTool serialises the call as JSON {"prompt": "..."} and passes it to A2AAgent.InvokeAsync.
var researcherTool = new AgentTool(
    remoteResearcher,
    name:        "researcher",
    description: "Calls the remote ResearchService to retrieve a research brief on any topic. " +
                 "Pass your research question as the 'prompt' field.");

// ── local writer agent ─────────────────────────────────────────────────────────

var model = new BedrockModel(region: Region, modelId: ModelId);

var writerAgent = new Agent(
    model,
    systemPrompt: """
        You are a science and technology writer for a general-interest publication.
        When you need factual background on a topic, use the 'researcher' tool to fetch
        a research brief from the research service.
        Once you have the research, write a compelling 3-paragraph article:
          Paragraph 1 — hook and context (why this topic matters now)
          Paragraph 2 — key findings and developments from the research
          Paragraph 3 — future outlook and implications for readers
        Write in an engaging, accessible style. Avoid jargon; explain technical terms briefly.
        """,
    tools: [researcherTool]);

// ── run ─────────────────────────────────────────────────────────────────────────

Console.WriteLine("WriterAgent calling ResearchService via A2A...");
Console.WriteLine();

Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("── Article ────────────────────────────────────────────────────────────");
Console.ResetColor();
Console.WriteLine();

await foreach (var evt in writerAgent.StreamAsync(
    $"Write an article about: {topic}", CancellationToken.None))
{
    switch (evt)
    {
        case TextDeltaEvent delta:
            Console.Write(delta.Delta);
            break;

        case ToolCallStartEvent toolStart:
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [A2A] → ResearchService: {toolStart.ToolName}(...)");
            Console.ResetColor();
            Console.WriteLine();
            break;

        case AgentCompleteEvent complete:
            Console.WriteLine();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  Tokens: {complete.Result.Usage.Total} " +
                              $"(in: {complete.Result.Usage.InputTokens}, " +
                              $"out: {complete.Result.Usage.OutputTokens})");
            Console.WriteLine($"  Tool calls: {complete.Result.Metrics.ToolCallCount}");
            Console.ResetColor();
            break;
    }
}

Console.WriteLine(new string('═', 70));
