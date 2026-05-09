using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;

// PersistentAssistant — demonstrates cross-run conversation persistence and
// automatic context summarization when the conversation grows too long.
//
// Architecture:
//   FileSessionManager          — stores session as JSON in ~/.strands/persistent-assistant/
//   SummarizingConversationManager — replaces old messages with a model-generated summary
//                                    once message count exceeds the threshold
//   AgentState                  — holds user preferences (name) that survive in the session
//
// SDK features shown:
//   • FileSessionManager        — disk-backed session save/load
//   • SummarizingConversationManager — async LLM-driven context trimming
//   • AgentState                — key-value state bag persisted alongside messages
//   • Manual session management — save with a stable ID so the same session loads each run
//
// Cross-run behaviour:
//   First run  — greets you as a new user, asks your name, starts fresh.
//   Later runs — loads prior conversation, greets you by name, continues where you left off.
//   Long runs  — once the conversation exceeds 8 messages the oldest ones are summarised
//                to keep the context window lean.
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage:
//   dotnet run                 (start or resume your session)
//   dotnet run -- --reset      (delete saved session and start fresh)

const string Region    = "us-east-1";
const string ModelId   = "us.anthropic.claude-haiku-4-5-20251001-v1:0";
const string SessionId = "persistent-session";

// Summarization threshold: deliberately low so a short demo triggers it.
// In production, use a higher value (e.g. 40) to match your context window budget.
const int SummarizationThreshold = 8;
const int KeepRecentMessages     = 4;

// ── storage ────────────────────────────────────────────────────────────────────

var sessionsDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".strands", "persistent-assistant");

var sessionManager = new FileSessionManager(sessionsDir);

// ── handle --reset flag ────────────────────────────────────────────────────────

if (args.Length > 0 && args[0] == "--reset")
{
    var path = Path.Combine(sessionsDir, $"{SessionId}.json");
    if (File.Exists(path))
    {
        File.Delete(path);
        Console.WriteLine("Session reset. Starting fresh.");
    }
    else
    {
        Console.WriteLine("No saved session found.");
    }
    Console.WriteLine();
}

// ── load prior session ─────────────────────────────────────────────────────────

var existingSession = await sessionManager.LoadAsync(SessionId);
var isNewSession    = existingSession is null;

// ── conversation manager + agent ───────────────────────────────────────────────

var model        = new BedrockModel(region: Region, modelId: ModelId);
var conversation = new SummarizingConversationManager(
    model,
    threshold:       SummarizationThreshold,
    keepRecentCount: KeepRecentMessages);

// Restore messages from the saved session so the agent remembers prior turns.
if (existingSession is not null)
{
    foreach (var msg in existingSession.Messages)
        conversation.Append(msg);
}

// Agent does not manage session saving itself (sessionManager omitted) —
// we save manually after each turn so we control the session ID.
var agent = new Agent(
    model,
    systemPrompt: """
        You are a helpful personal assistant with a good memory.
        You remember details the user shares across conversations because their history is preserved.
        Be warm, concise, and proactive — if the user mentions preferences or facts about themselves,
        acknowledge them and remember to reference them naturally in future turns.
        When asked what you remember, summarise the key facts from the conversation history.
        """,
    conversationManager: conversation);

// ── restore agent state ────────────────────────────────────────────────────────

// AgentState values survive as-is when we restore them from the JSON snapshot.
// We only rely on simple string values to avoid JsonElement round-trip issues.
if (existingSession is not null)
{
    foreach (var (key, value) in existingSession.State)
    {
        if (value is string s)
            agent.State.Set(key, s);
        else if (value?.ToString() is string sv)
            agent.State.Set(key, sv);
    }
}

// ── banner ─────────────────────────────────────────────────────────────────────

Console.WriteLine(new string('═', 60));
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("  Persistent Assistant");
Console.ResetColor();
Console.WriteLine(new string('═', 60));

if (isNewSession)
{
    Console.WriteLine("  New session — no prior history found.");
}
else
{
    var userName = agent.State.Get<string>("user.name");
    var greeting = userName is not null ? $"  Welcome back, {userName}!" : "  Resuming your session.";
    Console.WriteLine(greeting);
    Console.WriteLine($"  Restored {existingSession!.Messages.Count} messages from last session.");
    Console.WriteLine($"  Last active: {existingSession.LastUpdated:yyyy-MM-dd HH:mm} UTC");
}

Console.WriteLine($"  Sessions stored at: {sessionsDir}");
Console.WriteLine($"  Summarization threshold: {SummarizationThreshold} messages (keeping {KeepRecentMessages} recent)");
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("  Type 'quit' or press Ctrl+C to exit. Run with --reset to clear history.");
Console.ResetColor();
Console.WriteLine(new string('═', 60));
Console.WriteLine();

// ── REPL ───────────────────────────────────────────────────────────────────────

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var turnCount = 0;

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

    // Remember user's name if they introduce themselves.
    if (agent.State.Get<string>("user.name") is null)
    {
        var lower = input.ToLowerInvariant();
        var nameIdx = lower.IndexOf("my name is ", StringComparison.Ordinal);
        if (nameIdx >= 0)
        {
            var name = input[(nameIdx + 11)..].Split(' ')[0].Trim(',', '.', '!');
            if (name.Length > 0)
                agent.State.Set("user.name", name);
        }
    }

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("Assistant: ");
    Console.ResetColor();

    var messagesBefore = conversation.GetMessages().Count;

    try
    {
        await foreach (var evt in agent.StreamAsync(input, cts.Token).ConfigureAwait(false))
        {
            if (evt is TextDeltaEvent delta)
                Console.Write(delta.Delta);
        }
    }
    catch (OperationCanceledException)
    {
        break;
    }

    Console.WriteLine();
    Console.WriteLine();
    turnCount++;

    // ── trim if needed ─────────────────────────────────────────────────────────

    var messagesAfter = conversation.GetMessages().Count;
    var wasTrimmed    = false;

    if (messagesAfter > SummarizationThreshold)
    {
        await conversation.TrimAsync(cts.Token).ConfigureAwait(false);
        wasTrimmed = conversation.GetMessages().Count < messagesAfter;
    }

    if (wasTrimmed)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  [memory] Conversation summarized: " +
                          $"{messagesAfter} messages → {conversation.GetMessages().Count} messages");
        Console.ResetColor();
        Console.WriteLine();
    }

    // ── save session ───────────────────────────────────────────────────────────

    var session = new AgentSession(
        SessionId:   SessionId,
        Messages:    conversation.GetMessages(),
        State:       agent.State.ToSnapshot(),
        LastUpdated: DateTimeOffset.UtcNow);

    await sessionManager.SaveAsync(SessionId, session, cts.Token).ConfigureAwait(false);

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  [session] Saved — {conversation.GetMessages().Count} messages " +
                      $"| turn {turnCount} | {session.LastUpdated:HH:mm:ss} UTC");
    Console.ResetColor();
    Console.WriteLine();
}

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine($"Session saved. {turnCount} turn(s) this session.");
Console.ResetColor();
