// ═══════════════════════════════════════════════════════════════════════════════
// PATTERN: Decomposed Sequential Pipeline with Platform-Managed Durability
// ═══════════════════════════════════════════════════════════════════════════════
//
// This is Stage 2 of 3 — the most expensive stage, and the one that most
// benefits from platform-managed durability.
//
// WHY THIS STAGE IS THE DURABILITY BOTTLENECK:
// ExecuteAgent makes one LLM call per focus area (3 areas = 3 Bedrock calls),
// each taking 5-15 seconds. Total: 15-45 seconds of LLM work. In a real
// implementation with external API calls (search, databases, knowledge bases),
// this could easily be 5-10 minutes.
//
// WITHOUT DURABILITY (single Lambda):
// If this stage fails at focus area 3 after completing areas 1 and 2,
// the entire pipeline restarts from Stage 1. All prior LLM work is lost.
// Cost: 2 wasted Bedrock calls + Stage 1's call = 3 calls wasted per failure.
//
// WITH STEP FUNCTIONS DURABILITY:
// Stage 1's output (the ResearchPlan) is stored in the execution state.
// If this Lambda fails, Step Functions retries THIS stage only.
// Stage 1 is never re-run. The retry starts with the same ResearchPlan input.
// Cost: only the failed focus areas are re-researched.
//
// FAILURE SIMULATION:
// Set SIMULATE_FAILURE=true on this Lambda to trigger a deliberate failure
// on the first invocation. Step Functions will retry automatically.
// This demonstrates the retry behavior without needing a real failure.
//
// AGENT ROLE — ExecuteAgent (Stage 2):
// Receives the ResearchPlan from Stage 1. For each focus area, calls the
// Research tool to gather findings. Returns all findings as a structured
// ResearchFindings record for Stage 3 (SummarizeAgent) to synthesize.
//
// AGENT KNOWLEDGE:
// This agent has NO knowledge of Stage 1 or Stage 3. It does not call them.
// It receives a ResearchPlan, researches it, and returns ResearchFindings.
// ═══════════════════════════════════════════════════════════════════════════════

using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using System.Text.Json.Serialization;
using ExecuteAgent;

var handler = async (ResearchPlan input, ILambdaContext context) =>
{
    context.Logger.LogInformation(
        $"[Stage 2/3 — ExecuteAgent] Researching {input.FocusAreas.Length} focus areas for '{input.Topic}'");

    // ── Failure simulation ────────────────────────────────────────────────────
    // Set SIMULATE_FAILURE=true on this Lambda to demonstrate Step Functions retry.
    // On first invocation: throws an exception → Step Functions retries.
    // On retry: SIMULATE_FAILURE is still true but the retry counter is tracked
    // externally — in a real scenario you'd use a flag in S3 or DynamoDB.
    // For this demo, set SIMULATE_FAILURE=false after observing the first failure.
    if (Environment.GetEnvironmentVariable("SIMULATE_FAILURE") == "true")
    {
        context.Logger.LogWarning(
            "[Stage 2/3 — ExecuteAgent] SIMULATE_FAILURE=true — throwing to demonstrate Step Functions retry. " +
            "Set SIMULATE_FAILURE=false to allow the retry to succeed.");
        throw new InvalidOperationException(
            "Simulated failure in ExecuteAgent. Step Functions will retry this stage. " +
            "Stage 1 (PlanAgent) output is preserved — it will NOT be re-run.");
    }

    var agent = new Agent(
        model: new BedrockModel(
            region: Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1",
            // Claude Sonnet 4.6 — chosen for superior tool use and instruction following.
            // Research execution requires precise tool calls across multiple focus areas.
            // Using the latest Sonnet here maximises research quality for the most expensive stage.
            modelId: "us.anthropic.claude-sonnet-4-6"),
        systemPrompt: """
            You are a research execution specialist. Given a specific research question,
            provide a focused, factual answer in 3-4 sentences. Be concrete and specific.
            Use the Research tool to look up information before answering.
            """,
        toolProviders: [new ResearchTools()]);

    // Research each focus area independently.
    // Each call is a separate LLM invocation — this is where the time accumulates.
    // In a real implementation, these could call external APIs, search engines,
    // vector databases, or knowledge bases.
    var findings = new List<FocusAreaFinding>();

    foreach (var area in input.FocusAreas)
    {
        context.Logger.LogInformation(
            $"[Stage 2/3 — ExecuteAgent] Researching: '{area.Name}' — {area.Question}");

        var result = await agent.InvokeAsync(
            $"Research question: {area.Question}\n" +
            $"Context: This is part of a broader study on '{input.Topic}'. {area.Rationale}");

        findings.Add(new FocusAreaFinding
        {
            Name = area.Name,
            Question = area.Question,
            Answer = result.Message
        });

        context.Logger.LogInformation(
            $"[Stage 2/3 — ExecuteAgent] Completed: '{area.Name}' ({result.Message.Length} chars)");
    }

    context.Logger.LogInformation(
        $"[Stage 2/3 — ExecuteAgent] All {findings.Count} focus areas researched. " +
        $"Passing to Stage 3 (SummarizeAgent) via Step Functions.");

    // This return value becomes the input to SummarizeAgent.
    // Step Functions stores it in the execution state.
    return new ResearchFindings
    {
        Topic = input.Topic,
        Objective = input.Objective,
        Findings = [.. findings]
    };
};

await LambdaBootstrapBuilder
    .Create(handler, new SourceGeneratorLambdaJsonSerializer<WorkflowJsonContext>())
    .Build()
    .RunAsync();

namespace ExecuteAgent
{
    public partial class ResearchTools
    {
        // In a real implementation, this tool would call a search API, RAG pipeline,
        // knowledge base, or external data source. For this demo it returns structured
        // placeholder findings that demonstrate the pattern without external dependencies.
        [Tool("Look up research information for a specific question or topic")]
        public string Research(string question) =>
            $"Research findings for '{question}': Current evidence shows significant activity " +
            $"in this area with multiple approaches being explored. Key factors include technical " +
            $"feasibility, adoption barriers, and integration complexity. Recent developments " +
            $"suggest accelerating progress with practical applications emerging in production environments.";
    }

    // ── Data contracts ────────────────────────────────────────────────────────
    // Input to Stage 2 (output of Stage 1)
    public class ResearchPlan
    {
        public string Topic { get; set; } = "";
        public string Objective { get; set; } = "";
        public FocusArea[] FocusAreas { get; set; } = [];
    }

    public class FocusArea
    {
        public string Name { get; set; } = "";
        public string Question { get; set; } = "";
        public string Rationale { get; set; } = "";
    }

    // Output of Stage 2 / Input to Stage 3
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

    [JsonSerializable(typeof(ResearchPlan))]
    [JsonSerializable(typeof(FocusArea))]
    [JsonSerializable(typeof(FocusArea[]))]
    [JsonSerializable(typeof(ResearchFindings))]
    [JsonSerializable(typeof(FocusAreaFinding))]
    [JsonSerializable(typeof(FocusAreaFinding[]))]
    public partial class WorkflowJsonContext : JsonSerializerContext { }
}
