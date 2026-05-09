using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.Tools;

// FileAgent — demonstrates Week 2 built-in file tools and context window trimming.
//
// Week 2 features shown:
//   • FileReadTool  — reads files scoped to a safe base directory (path traversal rejected)
//   • FileWriteTool — writes / appends files scoped to the same directory
//   • SlidingWindowStrategy — trims context when a long conversation would overflow the budget
//   • ModelException — caught and printed when the model call fails (e.g. throttling)
//
// The agent works in a temporary "workspace" directory that is cleaned up on exit.
// It is seeded with three starter notes so the agent has something to read.
//
// Prerequisites: AWS credentials configured.
//
// Usage:
//   dotnet run                        # uses the default prompt below
//   dotnet run -- "summarise note1"   # custom prompt

// ── workspace setup ───────────────────────────────────────────────────────────

var workspace = Path.Combine(Path.GetTempPath(), $"strands-workspace-{Guid.NewGuid():N}");
Directory.CreateDirectory(workspace);

try
{
    // Seed the workspace with some starter content.
    await File.WriteAllTextAsync(Path.Combine(workspace, "note1.md"),
        "# Project Ideas\n- Build a CLI agent\n- Add context window trimming\n- Write file tools");
    await File.WriteAllTextAsync(Path.Combine(workspace, "note2.md"),
        "# Meeting Notes — 2025-01-15\nDiscussed: Bedrock throttling, retry policy, Polly integration.");
    await File.WriteAllTextAsync(Path.Combine(workspace, "note3.md"),
        "# Bugs\n- SlidingWindowStrategy off-by-one when budget equals one message\n- FIXED in commit a3f91b");

    Console.WriteLine($"Workspace: {workspace}");
    Console.WriteLine("Files: note1.md, note2.md, note3.md");
    Console.WriteLine();

    // ── model + tools ─────────────────────────────────────────────────────────

    // Week 2: FileReadTool and FileWriteTool scoped to the workspace directory.
    // Requests that escape the workspace (e.g. "../../etc/passwd") are rejected.
    var readTool  = new FileReadTool(workspace);
    var writeTool = new FileWriteTool(workspace);

    // Week 2: SlidingWindowStrategy trims the conversation to stay within the
    // token budget. The first message (system context) and the most recent
    // message are always preserved; middle messages are dropped first.
    var config = new AgentConfig
    {
        ContextWindowStrategy = new SlidingWindowStrategy(),
        MaxContextTokens = 4_000,   // low budget so trimming kicks in early
    };

    var model = new BedrockModel(region: "us-east-1",
        modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

    var agent = new Agent(
        model,
        systemPrompt: $"""
            You are a helpful note-taking assistant.
            The user's notes are stored in the workspace directory.
            Available files: note1.md, note2.md, note3.md.
            When asked to summarise or update notes, use the file tools.
            Always read a file before editing it.
            """,
        tools: [readTool, writeTool],
        config: config);

    // ── run ───────────────────────────────────────────────────────────────────

    var prompt = args.Length > 0
        ? string.Join(" ", args)
        : "Please read note1.md and note2.md, then write a combined summary to summary.md";

    Console.WriteLine($"Prompt: {prompt}");
    Console.WriteLine(new string('─', 60));

    try
    {
        await foreach (var evt in agent.StreamAsync(prompt))
        {
            switch (evt)
            {
                case TextDeltaEvent td:
                    Console.Write(td.Delta);
                    break;

                case ToolCallStartEvent tc:
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  ▶ {tc.ToolName}");
                    Console.ResetColor();
                    break;

                case ToolCallResultEvent tr:
                    Console.ForegroundColor = tr.Result.IsError ? ConsoleColor.Red : ConsoleColor.Green;
                    var preview = tr.Result.Content.Length > 80
                        ? tr.Result.Content[..80] + "…"
                        : tr.Result.Content;
                    Console.WriteLine($"  ◀ {preview}");
                    Console.ResetColor();
                    break;

                case AgentCompleteEvent complete:
                    Console.WriteLine();
                    Console.WriteLine(new string('─', 60));
                    Console.WriteLine($"Done  | stop: {complete.Result.StopReason,-12} " +
                                      $"| iters: {complete.Result.Metrics.Iterations} " +
                                      $"| tokens: {complete.Result.Usage.Total}");
                    break;
            }
        }
    }
    // Week 2: ModelException carries the HTTP status code and the original request.
    catch (ModelException ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Model error (HTTP {ex.HttpStatusCode}): {ex.Message}");
        Console.ResetColor();
    }

    // Print the workspace contents so we can see what was written.
    Console.WriteLine();
    Console.WriteLine("Workspace contents after run:");
    foreach (var file in Directory.GetFiles(workspace))
    {
        var info = new FileInfo(file);
        Console.WriteLine($"  {info.Name} ({info.Length} bytes)");
    }
}
finally
{
    Directory.Delete(workspace, recursive: true);
}
