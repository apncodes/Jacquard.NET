using System.Diagnostics;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.MultiAgent;

const string ModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
const string Topic   = "the impact of large language models on software development";

var model = new BedrockModel(region: "us-east-1", modelId: ModelId);

// ── Agent factories ──────────────────────────────────────────────────────────

Agent Researcher(string angle = "") => new(model, systemPrompt: $"""
    You are a research analyst{(angle.Length > 0 ? $" focusing on the {angle} angle" : "")}.
    When given a topic, produce 4-5 concise bullet-point findings based on your knowledge.
    Be specific and factual. No prose — bullet points only.
    """);

Agent Writer() => new(model, systemPrompt: """
    You are a technical writer. You receive research bullet points and write
    one clear, well-structured paragraph (5-7 sentences) for a developer audience.
    Do not add new facts — polish and connect what you are given.
    """);

Agent Reviewer() => new(model, systemPrompt: """
    You are a senior editor. You receive a draft paragraph and provide exactly
    three specific, actionable improvement suggestions as numbered points.
    Be direct and constructive — no praise, just improvements.
    """);

void Banner(string text) {
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n{'═'.ToString().PadRight(60, '═')}");
    Console.WriteLine($"  {text}");
    Console.WriteLine($"{'═'.ToString().PadRight(60, '═')}");
    Console.ResetColor();
}

void Label(string text) {
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n── {text} {new string('─', Math.Max(0, 55 - text.Length))}");
    Console.ResetColor();
}

void Dim(string text) {
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine(text);
    Console.ResetColor();
}

// ════════════════════════════════════════════════════════════════════════════
// PART A — Sequential pipeline: Researcher → Writer → Reviewer
// ════════════════════════════════════════════════════════════════════════════

Banner("Part A — Sequential Pipeline  (Researcher → Writer → Reviewer)");
Console.WriteLine($"\nTopic: {Topic}\n");

var pipeline = new PipelineOrchestrator([
    (Researcher(), "Researcher"),
    (Writer(),     "Writer"),
    (Reviewer(),   "Reviewer"),
]);

await foreach (var staged in pipeline.StreamAsync(Topic))
{
    switch (staged.Event)
    {
        case TextDeltaEvent delta:
            Console.Write(delta.Delta);
            break;
        case AgentCompleteEvent complete:
            Console.WriteLine();
            Dim($"  ✓ {staged.StageName ?? $"Stage {staged.StageIndex}"} complete" +
                $" — {complete.Result.Usage.Total} tokens");
            Console.WriteLine();
            break;
    }
}

// ════════════════════════════════════════════════════════════════════════════
// PART B — Parallel researchers feeding into Writer → Reviewer
//          Timestamps prove the two agents run simultaneously.
// ════════════════════════════════════════════════════════════════════════════

Banner("Part B — Parallel Research  (2 agents × Task.WhenAll → Writer → Reviewer)");
Console.WriteLine($"\nTopic: {Topic}\n");

var sw = Stopwatch.StartNew();

string Ts() => $"+{sw.Elapsed.TotalSeconds:F1}s";

Dim($"[{Ts()}] Launching Researcher-1 (technology angle) and Researcher-2 (business angle) simultaneously...\n");

// Both researchers start at the same instant — timestamps in console will overlap.
var r1Task = Task.Run(async () =>
{
    var result = await Researcher("technology").InvokeAsync(Topic).ConfigureAwait(false);
    Dim($"[{Ts()}] Researcher-1 (technology) finished — {result.Usage.Total} tokens");
    return result.Message;
});

var r2Task = Task.Run(async () =>
{
    var result = await Researcher("business impact").InvokeAsync(Topic).ConfigureAwait(false);
    Dim($"[{Ts()}] Researcher-2 (business) finished — {result.Usage.Total} tokens");
    return result.Message;
});

var findings = await Task.WhenAll(r1Task, r2Task).ConfigureAwait(false);
Dim($"[{Ts()}] Both researchers done. Wall-clock time proves parallel execution.\n");

var combined = $"Technology findings:\n{findings[0]}\n\nBusiness findings:\n{findings[1]}";

Label("Writer — synthesising both research threads");
var draft = await Writer().InvokeAsync(combined).ConfigureAwait(false);
Console.WriteLine(draft.Message);
Dim($"  ✓ Writer complete — {draft.Usage.Total} tokens  [{Ts()}]");

Label("Reviewer — critique");
var review = await Reviewer().InvokeAsync(draft.Message).ConfigureAwait(false);
Console.WriteLine(review.Message);
Dim($"  ✓ Reviewer complete — {review.Usage.Total} tokens  [{Ts()}]");

Console.WriteLine();
Dim($"Total wall-clock time: {sw.Elapsed.TotalSeconds:F1}s");
