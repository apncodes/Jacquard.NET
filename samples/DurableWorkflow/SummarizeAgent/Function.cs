// ═══════════════════════════════════════════════════════════════════════════════
// PATTERN: Decomposed Sequential Pipeline with Platform-Managed Durability
// ═══════════════════════════════════════════════════════════════════════════════
//
// This is Stage 3 of 3 — the synthesis stage.
//
// AGENT ROLE — SummarizeAgent (Stage 3):
// Receives the ResearchFindings from Stage 2 — a structured set of answers
// to each focus area's research question. Uses an LLM to synthesize these
// into a coherent executive summary with key insights and conclusions.
//
// WHY A SEPARATE STAGE?
// Synthesis is a distinct cognitive task from research execution. Separating it:
// 1. Allows independent retry if synthesis fails (rare, but possible)
// 2. Keeps each agent's system prompt focused on one task
// 3. Makes the pipeline stages independently testable
// 4. Allows swapping the summarization model independently of the research model
//
// AGENT KNOWLEDGE:
// This agent has NO knowledge of Stage 1 or Stage 2. It does not call them.
// It receives ResearchFindings and returns a WorkflowResult.
// Step Functions has already ensured that the findings are complete and valid
// before invoking this stage.
// ═══════════════════════════════════════════════════════════════════════════════

using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Jacquard.Core;
using Jacquard.Models.Bedrock;
using System.Text.Json.Serialization;

var handler = async (ResearchFindings input, ILambdaContext context) =>
{
    context.Logger.LogInformation(
        $"[Stage 3/3 — SummarizeAgent] Synthesizing {input.Findings.Length} findings for '{input.Topic}'");

    var agent = new Agent(
        model: new BedrockModel(
            region: Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
            // Amazon Nova Pro — AWS-native model, chosen for synthesis speed and cost efficiency.
            // Summarization is a writing task, not a reasoning task — Nova Pro excels here.
            // This demonstrates that each pipeline stage can independently choose its model.
            modelId: "us.amazon.nova-pro-v1:0"),
        systemPrompt: """
            You are a research synthesis specialist. Given a set of research findings
            organized by focus area, produce a clear executive summary that:
            1. Opens with the overall conclusion (1-2 sentences)
            2. Highlights the 3 most important insights across all focus areas
            3. Closes with practical implications or next steps (1-2 sentences)

            Write for a technical audience. Be specific, not generic.
            Total length: 150-200 words.
            """);

    // Build a structured prompt from the findings
    var findingsText = string.Join("\n\n", input.Findings.Select(f =>
        $"Focus Area: {f.Name}\n" +
        $"Question: {f.Question}\n" +
        $"Findings: {f.Answer}"));

    var result = await agent.InvokeAsync(
        $"Synthesize these research findings into an executive summary.\n\n" +
        $"Topic: {input.Topic}\n" +
        $"Objective: {input.Objective}\n\n" +
        $"Research Findings:\n{findingsText}");

    context.Logger.LogInformation(
        $"[Stage 3/3 — SummarizeAgent] Pipeline complete. Summary: {result.Message.Length} chars.");

    // Final output of the entire pipeline — returned as the Step Functions execution result.
    return new WorkflowResult
    {
        Topic = input.Topic,
        Objective = input.Objective,
        Summary = result.Message,
        FocusAreas = input.Findings.Select(f => f.Name).ToArray()
    };
};

await LambdaBootstrapBuilder
    .Create(handler, new SourceGeneratorLambdaJsonSerializer<WorkflowJsonContext>())
    .Build()
    .RunAsync();

// ── Data contracts ────────────────────────────────────────────────────────────

// Input to Stage 3 (output of Stage 2)
public class ResearchFindings
{
    public string Topic { get; set; } = "";
    public string Objective { get; set; } = "";
    public FocusAreaFinding[] Findings { get; set; } = [];
}

public class FocusAreaFinding
{
    public string Name { get; set; } = "";
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
}

// Final output of the pipeline — the Step Functions execution result
public class WorkflowResult
{
    public string Topic { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Summary { get; set; } = "";
    public string[] FocusAreas { get; set; } = [];
}

[JsonSerializable(typeof(ResearchFindings))]
[JsonSerializable(typeof(FocusAreaFinding))]
[JsonSerializable(typeof(FocusAreaFinding[]))]
[JsonSerializable(typeof(WorkflowResult))]
[JsonSerializable(typeof(string[]))]
public partial class WorkflowJsonContext : JsonSerializerContext { }
