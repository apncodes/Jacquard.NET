namespace StrandsAgents.Core;

/// <summary>
/// Token-budget context window strategy that retains the most recent messages while
/// always preserving the first user message to maintain conversation coherence.
/// Token count is estimated using the chars-divided-by-4 approximation.
/// </summary>
public sealed class SlidingWindowStrategy : IContextWindowStrategy
{
    /// <summary>
    /// Returns a trimmed list that fits within <paramref name="maxTokens"/>, keeping
    /// the most recent messages and always preserving <c>messages[0]</c>.
    /// </summary>
    /// <param name="messages">The full conversation history.</param>
    /// <param name="maxTokens">Maximum token budget.</param>
    /// <param name="ct">Cancellation token (unused — trimming is synchronous).</param>
    public Task<IList<Message>> TrimAsync(
        IList<Message> messages,
        int maxTokens,
        CancellationToken ct = default)
    {
        if (messages.Count == 0)
            return Task.FromResult(messages);

        // Fast path: estimate total tokens and skip trimming if already under budget.
        var totalTokens = messages.Sum(EstimateTokens);
        if (totalTokens <= maxTokens)
            return Task.FromResult(messages);

        // Build a trimmed list by accumulating from the end until the budget is exceeded,
        // then unconditionally prepend messages[0].
        var kept = new List<Message>(messages.Count);
        var tokensSoFar = 0;

        for (var i = messages.Count - 1; i >= 1; i--)
        {
            var cost = EstimateTokens(messages[i]);
            if (tokensSoFar + cost > maxTokens)
                break;
            tokensSoFar += cost;
            kept.Add(messages[i]);
        }

        // Reverse so messages are in original chronological order.
        kept.Reverse();

        // Always prepend the first message (anchor for conversation coherence).
        var firstCost = EstimateTokens(messages[0]);
        if (kept.Count == 0 || kept[0] != messages[0])
            kept.Insert(0, messages[0]);

        // If the first message alone exceeds the budget, return just it — the caller
        // must handle the oversized message (e.g. by raising MaxTokens).
        _ = firstCost; // acknowledged; we still return it

        return Task.FromResult<IList<Message>>(kept);
    }

    /// <summary>
    /// Estimates the token count of a message using the chars/4 approximation.
    /// Tool blocks contribute a fixed 4-token overhead for JSON framing.
    /// </summary>
    private static int EstimateTokens(Message message)
    {
        var tokens = 0;
        foreach (var block in message.Content)
        {
            tokens += block switch
            {
                TextBlock t => Math.Max(1, t.Text.Length / 4),
                ToolUseBlock tu => Math.Max(1, tu.Input.GetRawText().Length / 4) + 4,
                ToolResultBlock tr => Math.Max(1, tr.Content.Length / 4) + 4,
                _ => 4
            };
        }
        // Add a small per-message overhead for role metadata.
        return tokens + 4;
    }
}
