using Jacquard.Core;
using Jacquard.Models.Bedrock;
using Jacquard.MultiAgent;
using SwarmResearch;

// SwarmResearch — technology article writing swarm demonstrating the Swarm pattern.
//
// Architecture:
//   4 specialist agents collaborate autonomously via dynamic handoffs:
//     Researcher   → ResearchTools_SearchFacts_Tool, ResearchTools_GetSources_Tool
//     Analyst      → (no tools — synthesises research into a structured outline)
//     Writer       → (no tools — drafts the article from the analysis)
//     Editor       → ResearchTools_ReviewDraft_Tool
//
//   Unlike ParallelOrchestrator (fixed fan-out/fan-in), the swarm has no predetermined
//   execution path. Each agent decides whether to hand off to a peer or terminate.
//   Shared context accumulates as agents contribute knowledge, so every agent sees
//   the full history of what has been done and what remains.
//
// SDK features shown:
//   • SwarmOrchestrator.StreamAsync — real-time IAsyncEnumerable<SwarmEvent> stream
//   • SwarmEvent hierarchy          — typed events for every lifecycle moment
//   • SwarmAgentNode                — named agent with optional description for routing hints
//   • [Tool] source generator       — compile-time ITool wrappers, zero reflection
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage:
//   dotnet run                          (defaults to "large language models")
//   dotnet run -- "quantum computing"
//   dotnet run -- "renewable energy"

var topic = args.Length > 0 && !args[0].StartsWith('-')
    ? args[0].ToLowerInvariant()
    : "large language models";

PrintBanner(topic);

// ── model ────────────────────────────────────────────────────────────────────────

const string Region  = "us-east-1";
const string ModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
var model = new BedrockModel(region: Region, modelId: ModelId);

// ── shared tool instance ─────────────────────────────────────────────────────────

var researchTools = new ResearchTools();

// ── specialist agents ────────────────────────────────────────────────────────────

var researcher = new Agent(model,
    systemPrompt: """
        You are a research specialist. Your job is to gather verified facts, statistics,
        and authoritative sources on the given topic using your available tools.
        Be thorough: call SearchFacts and GetSources for the topic.
        Summarise your findings clearly. Do not write the article — that is the writer's job.
        When you have gathered sufficient research, hand off to the analyst.
        """,
    toolProviders: [researchTools]);

var analyst = new Agent(model,
    systemPrompt: """
        You are a content analyst. You receive raw research and organise it into a
        structured outline: key themes, supporting evidence, narrative arc, and
        any gaps that need addressing.
        Do not write the full article — produce a clear outline and brief for the writer.
        When your outline is ready, hand off to the writer.
        """);

var writer = new Agent(model,
    systemPrompt: """
        You are a technology journalist. You receive a structured outline and research brief,
        then write a polished 400-600 word article suitable for a general technical audience.
        Use clear, engaging prose. Include a headline, introduction, body paragraphs, and conclusion.
        When your draft is complete, hand off to the editor for review.
        """);

var editor = new Agent(model,
    systemPrompt: """
        You are a senior editor. You receive a draft article and use your ReviewDraft tool
        to get editorial critique for the topic, then apply improvements:
        fix factual gaps, improve balance, sharpen the prose, and ensure the conclusion is strong.
        Produce the final, publication-ready version of the article.
        When you are satisfied with the article, terminate — do not hand off further.
        """,
    toolProviders: [researchTools]);

// ── swarm ────────────────────────────────────────────────────────────────────────

var swarm = new SwarmOrchestrator(
[
    new SwarmAgentNode("researcher", researcher, "Gathers verified facts and authoritative sources"),
    new SwarmAgentNode("analyst",   analyst,    "Organises research into a structured outline and brief"),
    new SwarmAgentNode("writer",    writer,     "Writes the article draft from the outline"),
    new SwarmAgentNode("editor",    editor,     "Reviews and polishes the draft into the final article"),
],
routingModel: model,
entryPoint: "researcher",
maxHandoffs: 10,
maxIterations: 12,
executionTimeout: TimeSpan.FromMinutes(10),
nodeTimeout: TimeSpan.FromMinutes(3),
repetitiveHandoffDetectionWindow: 6,
repetitiveHandoffMinUniqueAgents: 3);

// ── stream events ────────────────────────────────────────────────────────────────
//
// StreamAsync yields typed SwarmEvent records for every lifecycle moment.
// The application subscribes by iterating the IAsyncEnumerable<SwarmEvent>.

SwarmCompletedEvent? completed = null;
var currentAgentId = string.Empty;
var isStreamingText = false;

await foreach (var evt in swarm.StreamAsync(
    $"Write a well-researched, balanced technology article about: {topic}"))
{
    switch (evt)
    {
        case SwarmStartedEvent started:
            PrintSwarmStarted(started);
            break;

        case AgentStartedEvent agentStarted:
            currentAgentId = agentStarted.AgentId;
            isStreamingText = false;
            PrintAgentStarted(agentStarted);
            break;

        case AgentToolCallEvent toolCall:
            if (isStreamingText) { Console.WriteLine(); isStreamingText = false; }
            PrintToolCall(toolCall);
            break;

        case AgentToolResultEvent toolResult:
            PrintToolResult(toolResult);
            break;

        case AgentTextDeltaEvent textDelta:
            // Stream tokens inline — no newline until the agent completes
            if (!isStreamingText)
            {
                Console.ForegroundColor = ConsoleColor.White;
                isStreamingText = true;
            }
            Console.Write(textDelta.Delta);
            break;

        case AgentCompletedEvent agentCompleted:
            if (isStreamingText) { Console.WriteLine(); isStreamingText = false; }
            PrintAgentCompleted(agentCompleted);
            break;

        case HandoffEvent handoff:
            PrintHandoff(handoff);
            break;

        case SwarmCompletedEvent swarmCompleted:
            completed = swarmCompleted;
            break;
    }
}

// ── final summary ────────────────────────────────────────────────────────────────

if (completed is not null)
    PrintSummary(completed);

// ── console helpers ──────────────────────────────────────────────────────────────

static void PrintBanner(string topic)
{
    Console.WriteLine(new string('═', 70));
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  Research Article Swarm — \"{topic}\"");
    Console.ResetColor();
    Console.WriteLine(new string('═', 70));
    Console.WriteLine();
}

static void PrintSwarmStarted(SwarmStartedEvent e)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  Swarm started  →  entry point: {e.EntryAgentId}");
    Console.ResetColor();
    Console.WriteLine();
}

static void PrintAgentStarted(AgentStartedEvent e)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    var label = e.AgentId.ToUpperInvariant();
    Console.WriteLine(new string('─', 70));
    Console.Write($"  [{e.Iteration}] {label}");
    if (!string.IsNullOrWhiteSpace(e.Description))
        Console.Write($"  —  {e.Description}");
    Console.WriteLine();
    Console.ResetColor();

    if (!string.IsNullOrWhiteSpace(e.HandoffMessage))
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  ↳ Handoff: {e.HandoffMessage.Split('\n')[0].Trim()}");
        Console.ResetColor();
    }

    Console.WriteLine();
}

static void PrintToolCall(AgentToolCallEvent e)
{
    Console.ForegroundColor = ConsoleColor.DarkMagenta;
    Console.WriteLine($"  ⚙  {e.AgentId} → calling tool: {e.ToolName}");
    Console.ResetColor();
}

static void PrintToolResult(AgentToolResultEvent e)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    var preview = e.Result.Content?.Length > 80
        ? e.Result.Content[..80].Replace('\n', ' ') + "…"
        : e.Result.Content?.Replace('\n', ' ') ?? "(empty)";
    Console.WriteLine($"  ✓  tool result: {preview}");
    Console.ResetColor();
}

static void PrintAgentCompleted(AgentCompletedEvent e)
{
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  tokens: {e.Result.Usage.Total}  " +
                      $"(in: {e.Result.Usage.InputTokens} / out: {e.Result.Usage.OutputTokens})");
    Console.ResetColor();
    Console.WriteLine();
}

static void PrintHandoff(HandoffEvent e)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  ⟶  Handoff: {e.FromAgentId} → {e.ToAgentId}");
    Console.ResetColor();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    var preview = e.Message.Split('\n')[0].Trim();
    if (preview.Length > 100) preview = preview[..100] + "…";
    Console.WriteLine($"     \"{preview}\"");
    Console.ResetColor();
    Console.WriteLine();
}

static void PrintSummary(SwarmCompletedEvent e)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine(new string('═', 70));
    Console.WriteLine("  Swarm Complete");
    Console.WriteLine(new string('═', 70));
    Console.ResetColor();
    Console.WriteLine();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  Status:        {e.Status}");
    Console.WriteLine($"  Agents run:    {string.Join(" → ", e.NodeHistory.Select(n => n.AgentId))}");
    Console.WriteLine($"  Total tokens:  {e.TotalUsage.Total}  " +
                      $"(in: {e.TotalUsage.InputTokens} / out: {e.TotalUsage.OutputTokens})");
    Console.ResetColor();
    Console.WriteLine();

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  ── Final Article " + new string('─', 52));
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine(e.FinalMessage);
    Console.WriteLine();
    Console.WriteLine(new string('═', 70));
}
