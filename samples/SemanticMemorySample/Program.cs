using Jacquard.Core;
using Jacquard.Models.Bedrock;
using Jacquard.Runtime.Tools;

// SemanticMemorySample — demonstrates semantic (vector) memory retrieval via SemanticMemoryTool.
//
// Architecture:
//   SemanticMemoryTool  — exposes search_memory / store_memory / delete_memory to the LLM.
//                         search_memory retrieves memories by meaning, not exact key.
//   AgentCoreMemoryTool — key-value memory for comparison (exact-key retrieval).
//
// SDK features shown:
//   • SemanticMemoryTool — new tool backed by AgentCore Memory semantic search API.
//                          The LLM can call search_memory("user preferences") and get
//                          back the closest matches ranked by cosine similarity score.
//   • ttl_seconds        — store_memory accepts an optional TTL so sensitive facts
//                          expire automatically.
//   • SigV4 auth         — SemanticMemoryTool signs every HTTP request automatically
//                          using credentials from the standard AWS credential chain.
//
// Prerequisites:
//   • AWS credentials configured (env vars, ~/.aws/credentials, or IAM role)
//   • AgentCore Memory resource: memory_hrutx-EQoEXYAjAJ (us-west-2)
//
// Usage:
//   dotnet run --project samples/SemanticMemorySample

const string MemoryId = "memory_hrutx-EQoEXYAjAJ";
const string Region   = "us-west-2";
const string ModelId  = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

// ── tools ──────────────────────────────────────────────────────────────────────

// SemanticMemoryTool — SigV4-signed automatically; no clientOverride needed in production.
using var semanticMemory = new SemanticMemoryTool(MemoryId, region: Region);

// ── agent ──────────────────────────────────────────────────────────────────────

var model = new BedrockModel(region: Region, modelId: ModelId);

var agent = new Agent(
    model,
    systemPrompt: """
        You are a helpful personal assistant with access to a semantic memory store.

        Records are stored as free-text content with an optional namespace.
        The system assigns a memoryRecordId when you store a record — remember it if you need to delete it later.

        Before answering questions about the user, always call search_memory with a
        natural-language description of what you are looking for. The tool returns
        records ranked by relevance — use the top results to inform your answer.

        When the user shares new facts about themselves, store them with store_memory.
        Use a descriptive namespace like "user:preferences:coffee" or "user:projects".

        Be warm, concise, and reference stored memories naturally in your responses.
        """,
    tools: [semanticMemory]);

// ── seed some memories ─────────────────────────────────────────────────────────

Console.WriteLine(new string('═', 60));
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  Semantic Memory Sample");
Console.ResetColor();
Console.WriteLine(new string('═', 60));
Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Seeding sample memories...");
Console.ResetColor();

// Seed a few memories so the demo has something to search.
// In a real app the agent would store these itself via store_memory.
var seedPrompt = """
    Please store the following facts about the user using store_memory.
    Store each as a separate record with an appropriate namespace.

    1. content="User's name is Alex", namespace="user:profile"
    2. content="Alex prefers oat milk flat white, no sugar", namespace="user:preferences:coffee"
    3. content="Alex prefers concise answers with no bullet points", namespace="user:preferences:communication"
    4. content="Alex is working on a distributed agent system using Jacquard.NET", namespace="user:projects"
    5. content="Alex is in the Europe/London timezone", namespace="user:profile"

    Store each one separately. After storing, confirm with the memoryRecordId for each.
    """;

var seedResult = await agent.InvokeAsync(seedPrompt);
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine($"  Seed: {seedResult.Message[..Math.Min(80, seedResult.Message.Length)]}...");
Console.ResetColor();
Console.WriteLine();

// ── banner ─────────────────────────────────────────────────────────────────────

Console.WriteLine(new string('─', 60));
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Memory ID: " + MemoryId);
Console.WriteLine("  Try asking: 'What do you know about me?'");
Console.WriteLine("              'What coffee do I like?'");
Console.WriteLine("              'What project am I working on?'");
Console.WriteLine("  Type 'quit' or press Ctrl+C to exit.");
Console.ResetColor();
Console.WriteLine(new string('─', 60));
Console.WriteLine();

// ── REPL ───────────────────────────────────────────────────────────────────────

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

while (!cts.Token.IsCancellationRequested)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("You: ");
    Console.ResetColor();

    var input = Console.ReadLine();

    if (input is null || input.Trim().ToLowerInvariant() is "quit" or "exit" or "q")
        break;

    if (string.IsNullOrWhiteSpace(input))
        continue;

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Assistant: ");
    Console.ResetColor();

    try
    {
        await foreach (var evt in agent.StreamAsync(input, cts.Token).ConfigureAwait(false))
        {
            switch (evt)
            {
                case TextDeltaEvent delta:
                    Console.Write(delta.Delta);
                    break;

                case ToolCallStartEvent toolStart:
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write($"  [tool] {toolStart.ToolName}...");
                    Console.ResetColor();
                    break;

                case ToolCallResultEvent toolResult:
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    var preview = toolResult.Result.Content.Length > 60
                        ? toolResult.Result.Content[..60] + "..."
                        : toolResult.Result.Content;
                    Console.WriteLine($" → {preview}");
                    Console.ResetColor();
                    break;
            }
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }

    Console.WriteLine();
    Console.WriteLine();
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("Goodbye.");
Console.ResetColor();
