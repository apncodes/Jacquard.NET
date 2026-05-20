using Jacquard.Core;
using Jacquard.Models.Bedrock;
using Jacquard.MultiAgent;
using SupportTriage;

// SupportTriage — demonstrates conditional graph routing, type-safe hooks,
// source-generated tools, and structured output extraction.
//
// Architecture:
//   TriageAgent (first graph node)
//       ↓  AddConditionalEdge — parses "ROUTE: <category>" from triage output
//   BillingAgent | TechnicalAgent | AccountAgent | GeneralAgent
//       ↓  no outgoing edge → graph terminates naturally
//
// SDK features shown:
//   • GraphBuilder.AddConditionalEdge — LLM-driven routing between specialist agents
//   • [Tool] source generator          — TicketLookupTool_LookupTicket_Tool (compile-time ITool)
//   • BeforeToolCallEvent hook         — log + approve CRM lookups (set Interrupt=true for HITL)
//   • AfterModelCallEvent hook         — aggregate token usage across the full graph run
//   • GetStructuredOutputAsync<T>      — typed extraction from the specialist's response
//
// Prerequisites: AWS credentials configured (env vars, ~/.aws/credentials, or IAM role).
//
// Usage:
//   dotnet run

const string Region  = "us-east-1";
const string ModelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0";

// ── model ─────────────────────────────────────────────────────────────────────

var model = new BedrockModel(region: Region, modelId: ModelId);

// ── tool ──────────────────────────────────────────────────────────────────────

// TicketLookupTool_LookupTicket_Tool is generated at compile time by Strands.SourceGenerator.
// The JSON schema is a string literal baked into the generated class — zero runtime reflection.
ITool[] specialistTools = [new TicketLookupTool_LookupTicket_Tool(new TicketLookupTool())];

// ── sample tickets ────────────────────────────────────────────────────────────

var tickets = new (string Id, string Text)[]
{
    ("TKT-001",
        "I was charged twice for my Pro subscription this month — once on April 1st and " +
        "again on April 3rd. Both charges are $49.99. Please refund the duplicate. " +
        "My ticket reference is TKT-001."),

    ("TKT-002",
        "The app crashes immediately every time I launch it since the 3.1.2 update on April 12th. " +
        "I'm on iPhone 14 Pro, iOS 17.4. Uninstalling and reinstalling didn't help. " +
        "My ticket reference is TKT-002."),

    ("TKT-003",
        "I recently changed companies and need to update my account email address. " +
        "I've already verified my identity via the support PIN. " +
        "My ticket reference is TKT-003."),
};

// ── process each ticket ───────────────────────────────────────────────────────

foreach (var (ticketId, ticketText) in tickets)
{
    Console.WriteLine(new string('═', 70));
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Ticket: {ticketId}");
    Console.ResetColor();
    Console.WriteLine(ticketText);
    Console.WriteLine();

    // ── per-ticket hooks ──────────────────────────────────────────────────────

    var inputTokens = 0;
    var outputTokens = 0;

    var hooks = new HookRegistry();

    // Fires before every CRM lookup. To require human approval instead of auto-approving,
    // uncomment the Console.ReadLine block and set e.Interrupt = true on rejection.
    hooks.Register<BeforeToolCallEvent>(e =>
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"  [hook] CRM lookup approved: {e.Call.Name}({e.Call.Input})");
        Console.ResetColor();

        // HITL pattern — uncomment to require operator approval:
        // Console.Write("  Approve? [Y/n]: ");
        // if (Console.ReadLine()?.Trim().ToLower() == "n") e.Interrupt = true;

        return Task.CompletedTask;
    });

    hooks.Register<AfterModelCallEvent>(e =>
    {
        inputTokens  += e.Response.Usage.InputTokens;
        outputTokens += e.Response.Usage.OutputTokens;
        return Task.CompletedTask;
    });

    // ── build graph ───────────────────────────────────────────────────────────

    // Each call to BuildGraph creates fresh Agent instances so conversation history
    // from one ticket does not bleed into the next.
    var graph = BuildGraph(model, specialistTools, hooks);

    // ── run graph ─────────────────────────────────────────────────────────────

    var result = await graph.RunAsync($"Ticket ID: {ticketId}\n\n{ticketText}");

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("Specialist response:");
    Console.ResetColor();
    Console.WriteLine(result.Message);
    Console.WriteLine();
    Console.WriteLine($"  Tokens: {inputTokens + outputTokens} (in: {inputTokens}, out: {outputTokens})");

    // ── structured extraction ─────────────────────────────────────────────────

    // GetStructuredOutputAsync<T> runs in an isolated message list — it does not
    // pollute this agent's conversation history, so it's safe to create a fresh Agent
    // here without any tools and use it purely for schema-constrained extraction.
    var extractor = new Agent(model);
    var resolution = await extractor.GetStructuredOutputAsync<TicketResolution>(
        $"Extract structured resolution details from this support response:\n\n{result.Message}");

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  Extracted → Category: {resolution.Category} | " +
                      $"Action: {resolution.NextAction} | Resolved: {resolution.IsResolved}");
    Console.ResetColor();
    Console.WriteLine();
}

Console.WriteLine(new string('═', 70));
Console.WriteLine("All tickets processed.");

// ── graph factory ─────────────────────────────────────────────────────────────

static AgentGraph BuildGraph(IModel model, ITool[] tools, HookRegistry hooks)
{
    // TriageAgent: classifies the ticket and starts its response with "ROUTE: <category>".
    // The rest of its message becomes the prompt for the specialist (passed via AgentGraph's
    // currentPrompt = lastResult.Message path), giving the specialist full routing context.
    var triage = new Agent(model, systemPrompt: """
        You are a support ticket triage agent.
        Your response MUST begin with exactly this line:
        ROUTE: <category>

        Replace <category> with one of: billing, technical, account, general.
        Choose the best match for the issue described.

        After the ROUTE line, write 2-3 sentences summarising the issue and
        any ticket ID or reference numbers the customer mentioned.
        No other text before the ROUTE line.
        """);

    Agent Specialist(string domain, string instructions) => new(model,
        systemPrompt: $"""
            You are a {domain} support specialist.
            {instructions}
            When the customer mentions a ticket ID (format: TKT-XXX), use the LookupTicket tool
            to retrieve their account details before responding.
            Write a clear, empathetic response of 3–4 sentences. Address the customer directly.
            """,
        tools: tools,
        hooks: hooks);

    return new GraphBuilder()
        .AddNode("triage", triage)
        .AddNode("billing", Specialist("billing",
            "You handle charges, refunds, invoices, and subscription payments."))
        .AddNode("technical", Specialist("technical",
            "You handle app crashes, bugs, performance issues, and error messages."))
        .AddNode("account", Specialist("account",
            "You handle email updates, password resets, and account settings."))
        .AddNode("general", Specialist("general",
            "You handle general enquiries not covered by other specialist teams."))
        // Parse "ROUTE: <category>" from the first line of the triage agent's response.
        // Nodes with no outgoing edge cause AgentGraph to terminate and return their result.
        .AddConditionalEdge("triage", result =>
        {
            var firstLine = result.Message
                .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty;

            if (!firstLine.StartsWith("ROUTE:", StringComparison.OrdinalIgnoreCase))
                return "general";

            return firstLine["ROUTE:".Length..].Trim().ToLowerInvariant() switch
            {
                "billing"   => "billing",
                "technical" => "technical",
                "account"   => "account",
                _           => "general",
            };
        })
        .Build();
}

// ── typed extraction record ───────────────────────────────────────────────────

/// <summary>Structured summary extracted from the specialist's support response.</summary>
internal record TicketResolution(
    /// <summary>One of: Billing, Technical, Account, General.</summary>
    string Category,
    /// <summary>The primary action taken or recommended.</summary>
    string NextAction,
    /// <summary>Whether the issue was fully resolved in this response.</summary>
    bool IsResolved);
