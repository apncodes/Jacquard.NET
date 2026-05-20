using Jacquard.Core;
using Jacquard.Models.Bedrock;
using Jacquard.Tools;
using QuickTools;

// ── Model ─────────────────────────────────────────────────────────────────────
// Swap BedrockModel for AnthropicModel, OpenAICompatibleModel, or GeminiModel
// without changing anything else.
// ─────────────────────────────────────────────────────────────────────────────

var model = new BedrockModel(
    region: "us-east-1",
    modelId: "us.anthropic.claude-haiku-4-5-20251001-v1:0");

// ── Agent ─────────────────────────────────────────────────────────────────────
// Three tool providers:
//   - CalculatorTool    — built-in, from Jacquard.Tools
//   - CurrentTimeTool   — custom, defined below
//   - LetterCounterTool — custom, defined below
// ─────────────────────────────────────────────────────────────────────────────

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

// ── Custom tools ──────────────────────────────────────────────────────────────
// Decorate a method with [Tool] inside a partial class and the source generator
// emits a fully-typed ITool wrapper with a JSON schema at compile time.
// XML doc comments on the method and its parameters become the descriptions
// the model sees when deciding which tool to call.
// ─────────────────────────────────────────────────────────────────────────────

namespace QuickTools
{
    /// <summary>Counts occurrences of a specific letter in a word.</summary>
    public sealed partial class LetterCounterTool
    {
        /// <summary>Counts occurrences of a specific letter in a word.</summary>
        /// <param name="word">The input word to search in.</param>
        /// <param name="letter">The single character to count.</param>
        /// <returns>The number of times <paramref name="letter"/> appears in <paramref name="word"/>.</returns>
        [Tool("Count occurrences of a specific letter in a word.")]
        public int CountLetter(string word, string letter)
        {
            if (letter.Length != 1)
                throw new ArgumentException("The 'letter' parameter must be a single character.", nameof(letter));

            return word.ToLowerInvariant().Count(c => c == char.ToLowerInvariant(letter[0]));
        }
    }

    /// <summary>Returns the current UTC date and time.</summary>
    public sealed partial class CurrentTimeTool
    {
        /// <summary>Returns the current UTC date and time.</summary>
        [Tool("Returns the current UTC date and time.")]
        public string GetCurrentTime() =>
            DateTimeOffset.UtcNow.ToString("dddd, MMMM d, yyyy HH:mm:ss 'UTC'");
    }
}
