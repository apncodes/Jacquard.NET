using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;
using StrandsAgents.Tools;

// ── Agent ─────────────────────────────────────────────────────────────────────
// Three tool providers:
//   - CalculatorTool    (built-in, from StrandsAgents.Tools)
//   - CurrentTimeTool   (custom, defined in Tools.cs)
//   - LetterCounterTool (custom, defined in Tools.cs)
//
// Python equivalent:
//   agent = Agent(tools=[calculator, current_time, letter_counter])
// ─────────────────────────────────────────────────────────────────────────────

var model = new BedrockModel(
    region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

var agent = new Agent(
    model,
    toolProviders: [new CalculatorTool(), new CurrentTimeTool(), new LetterCounterTool()]);

var message = """
    I have 3 requests:
    1. What is the time right now?
    2. Calculate 3111696 / 74088
    3. Tell me how many letter R's are in the word "strawberry" 🍓
    """;

Console.WriteLine($"User: {message}");
Console.WriteLine();
Console.Write("Agent: ");

await foreach (var evt in agent.StreamAsync(message))
{
    if (evt is TextDeltaEvent delta)
        Console.Write(delta.Delta);
}

Console.WriteLine();
