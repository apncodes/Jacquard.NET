using Jacquard.Core;
using Jacquard.Models.Bedrock;
using Jacquard.MultiAgent;

// OrchestratedResearch — demonstrates Week 2 multi-agent orchestration patterns.
//
// Week 2 features shown:
//   • PipelineOrchestrator — chains agents sequentially; each stage receives the
//                            previous stage's output as its input prompt.
//   • ParallelOrchestrator — runs multiple agents concurrently with Task.WhenAll
//                            and collects all results.
//   • AgentTool            — wraps an IAgent as an ITool so a parent agent can
//                            delegate subtasks to a specialised child agent.
//   • PipelineStageEvent   — typed wrapper around StreamEvent with StageIndex/Name
//                            for fan-out streaming of a multi-stage pipeline.
//
// The scenario: a two-stage research pipeline.
//   Stage 1 (Researcher) — explores a topic and produces structured notes.
//   Stage 2 (Writer)     — turns those notes into a polished summary paragraph.
//
// We also run Stage 1 in parallel on two topics to show ParallelOrchestrator.
//
// Prerequisites: AWS credentials configured.

var model = new BedrockModel(region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// ── shared agent factory ──────────────────────────────────────────────────────

Agent MakeResearcher(string topic) => new(
    model,
    systemPrompt: $"""
        You are a research assistant specialising in {topic}.
        When given a question, produce 3-5 concise bullet-point notes.
        Do not write prose yet — just facts and key points.
        """);

Agent MakeWriter() => new(
    model,
    systemPrompt: """
        You are a technical writer. You receive bullet-point research notes
        and convert them into one clear, well-structured paragraph suitable
        for a general audience. Do not add new facts — only reorganise and
        polish what you are given.
        """);

// ── Part A: PipelineOrchestrator (sequential) ─────────────────────────────────

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("Part A — PipelineOrchestrator  (research → write)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();

// Week 2: PipelineOrchestrator — two named stages.
// The writer receives the researcher's bullet points as its prompt.
var pipeline = new PipelineOrchestrator(
[
    (MakeResearcher("distributed systems"), "Researcher"),
    (MakeWriter(),                          "Writer"),
]);

Console.WriteLine("Streaming pipeline events:");
Console.WriteLine();

// Week 2: StreamAsync yields PipelineStageEvent wrappers so you can
// attribute each event to the correct stage.
await foreach (var stageEvent in pipeline.StreamAsync(
    "What are the key trade-offs in the CAP theorem?"))
{
    switch (stageEvent.Event)
    {
        case TextDeltaEvent td:
            if (stageEvent.StageIndex == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write(td.Delta);
                Console.ResetColor();
            }
            else
            {
                Console.Write(td.Delta);
            }
            break;

        case AgentCompleteEvent complete:
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  [{stageEvent.StageName ?? $"Stage {stageEvent.StageIndex}"}] " +
                              $"complete — {complete.Result.Usage.Total} tokens");
            Console.ResetColor();
            Console.WriteLine();
            break;
    }
}

// ── Part B: ParallelOrchestrator ─────────────────────────────────────────────

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("Part B — ParallelOrchestrator  (two topics in parallel)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();

// Week 2: ParallelOrchestrator — same prompt sent to both agents concurrently.
var parallel = new ParallelOrchestrator(
[
    MakeResearcher("cloud cost optimisation"),
    MakeResearcher("developer productivity"),
]);

Console.WriteLine("Running two researchers in parallel…");
Console.WriteLine();

var results = await parallel.RunAsync(
    "List the three most impactful practices in your domain.");

for (var i = 0; i < results.Count; i++)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Agent {i + 1} result:");
    Console.ResetColor();
    Console.WriteLine(results[i].Message);
    Console.WriteLine();
}

// ── Part C: AgentTool (agent-as-tool inside a parent agent) ──────────────────

Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine("Part C — AgentTool  (sub-agent as a tool)");
Console.WriteLine("═══════════════════════════════════════════════════════════");
Console.WriteLine();

// Week 2: AgentTool wraps a child agent so a parent agent can call it as
// a regular tool.  The parent sends a JSON {"prompt": "..."} to the tool;
// the tool invokes the child agent and returns its text response.
var researcherTool = new AgentTool(
    MakeResearcher("software architecture"),
    name: "researcher",
    description: "Researches a software architecture topic and returns structured notes. " +
                 "Pass the topic or question as the 'prompt' field.");

var orchestratorAgent = new Agent(
    model,
    systemPrompt: """
        You are an executive assistant. When you need domain knowledge,
        delegate to the 'researcher' tool. Then summarise the notes in
        two sentences for a non-technical audience.
        """,
    tools: [researcherTool]);

Console.WriteLine("Parent agent delegating to researcher sub-agent via AgentTool…");
Console.WriteLine();

var orchestratorResult = await orchestratorAgent.InvokeAsync(
    "I need a quick overview of microservices vs monoliths for my manager.");

Console.WriteLine(orchestratorResult.Message);
Console.WriteLine();
Console.WriteLine($"Total iterations (parent): {orchestratorResult.Metrics.Iterations}");
Console.WriteLine($"Total tool calls  (parent): {orchestratorResult.Metrics.ToolCallCount}");
