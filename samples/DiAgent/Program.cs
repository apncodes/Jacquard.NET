using Microsoft.Extensions.DependencyInjection;
using Jacquard.Core;
using Jacquard.Extensions.DI;

// DiAgent — demonstrates Week 2 dependency-injection extensions.
//
// Week 2 features shown:
//   • AddBedrockModel()              — registers IModel as BedrockModel singleton
//   • AddFileReadTool()              — registers FileReadTool scoped to a safe directory
//   • AddFileWriteTool()             — registers FileWriteTool scoped to the same directory
//   • AddJacquardAgent()              — wires IAgent from the container, injecting model + tools
//   • AddJacquardInMemorySessionManager() — ISessionManager persists sessions to an in-memory store
//   • AgentConfig (via configure delegate) — sets SlidingWindowStrategy + MaxContextTokens
//
// The sample resolves one agent from the container and runs a two-turn conversation.
// Because the agent's InMemoryConversationManager accumulates turn history in-process,
// the second turn can reference what happened in the first.
// The ISessionManager stores a snapshot after each turn (load-and-restore is a future feature).
//
// Prerequisites: AWS credentials configured.

// ── workspace ─────────────────────────────────────────────────────────────────

var workspace = Path.Combine(Path.GetTempPath(), $"strands-diagent-{Guid.NewGuid():N}");
Directory.CreateDirectory(workspace);
await File.WriteAllTextAsync(Path.Combine(workspace, "readme.md"),
    "# My Project\nThis is a sample project managed by DiAgent.");

try
{
    // ── DI container setup ────────────────────────────────────────────────────

    var services = new ServiceCollection();

    // Week 2: register model, tools, session manager, and agent through extension methods.
    services
        .AddBedrockModel(region: "us-east-1",
            modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0")
        .AddFileReadTool(workspace)
        .AddFileWriteTool(workspace)
        .AddJacquardInMemorySessionManager()
        .AddJacquardAgent(new AgentConfig
        {
            // Week 2: configure context window trimming via the AgentConfig record.
            ContextWindowStrategy = new SlidingWindowStrategy(),
            MaxContextTokens = 8_000,
        });

    await using var provider = services.BuildServiceProvider();

    // Resolve a single agent. IAgent is registered as transient, so this instance
    // owns its own InMemoryConversationManager — multi-turn history accumulates here.
    var agent = provider.GetRequiredService<IAgent>();

    // ── turn 1 ────────────────────────────────────────────────────────────────

    Console.WriteLine("=== Turn 1 — read and update the file ===");
    Console.WriteLine();

    var result1 = await agent.InvokeAsync(
        "Read readme.md and tell me what you see. Then add a line 'Status: active' to it.");

    Console.WriteLine(result1.Message);
    Console.WriteLine();
    Console.WriteLine($"Tokens: {result1.Usage.Total} | Iterations: {result1.Metrics.Iterations}");
    Console.WriteLine();

    // ── turn 2 (same agent instance — conversation history is preserved) ───────

    Console.WriteLine("=== Turn 2 — follow-up question using conversation history ===");
    Console.WriteLine();

    var result2 = await agent.InvokeAsync(
        "What file did you just edit? What line did you add to it?");

    Console.WriteLine(result2.Message);
    Console.WriteLine();
    Console.WriteLine($"Tokens: {result2.Usage.Total} | Iterations: {result2.Metrics.Iterations}");

    // ── show DI registrations that were used ─────────────────────────────────

    Console.WriteLine();
    Console.WriteLine("=== Registered tools (resolved from DI) ===");
    foreach (var tool in provider.GetServices<ITool>())
        Console.WriteLine($"  • {tool.Definition.Name}");

    // ── show final file state ─────────────────────────────────────────────────

    Console.WriteLine();
    Console.WriteLine("=== Final readme.md ===");
    Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(workspace, "readme.md")));
}
finally
{
    Directory.Delete(workspace, recursive: true);
}
