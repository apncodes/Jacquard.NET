namespace Jacquard.Core;

/// <summary>Token consumption for a model call.</summary>
public record TokenUsage(int InputTokens, int OutputTokens)
{
    /// <summary>Total tokens consumed.</summary>
    public int Total => InputTokens + OutputTokens;

    public static TokenUsage operator +(TokenUsage a, TokenUsage b) =>
        new(a.InputTokens + b.InputTokens, a.OutputTokens + b.OutputTokens);

    public static readonly TokenUsage Zero = new(0, 0);
}
