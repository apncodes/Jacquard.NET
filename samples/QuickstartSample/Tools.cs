using StrandsAgents.Core;

// ── Custom tools ──────────────────────────────────────────────────────────────
// The [Tool] attribute on a method inside a partial class is all the source
// generator needs to emit a fully-typed ITool wrapper with a JSON schema.
// This is the .NET equivalent of Python's @tool decorator.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Custom tool that counts occurrences of a specific letter in a word.
/// .NET equivalent of the Python @tool letter_counter function.
/// </summary>
public sealed partial class LetterCounterTool
{
    /// <summary>
    /// Counts occurrences of a specific letter in a word.
    /// </summary>
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

/// <summary>
/// Custom tool that returns the current UTC date and time.
/// .NET equivalent of strands_tools.current_time.
/// </summary>
public sealed partial class CurrentTimeTool
{
    /// <summary>Returns the current UTC date and time.</summary>
    [Tool("Returns the current UTC date and time.")]
    public string GetCurrentTime() =>
        DateTimeOffset.UtcNow.ToString("dddd, MMMM d, yyyy HH:mm:ss 'UTC'");
}
