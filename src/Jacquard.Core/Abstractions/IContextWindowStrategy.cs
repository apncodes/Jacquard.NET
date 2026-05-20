namespace Jacquard.Core;

/// <summary>
/// Strategy for trimming the conversation message list to fit within a token budget
/// before each model call. Implementations may drop messages, summarize, or use any
/// other approach to reduce token usage while preserving conversation coherence.
/// </summary>
public interface IContextWindowStrategy
{
    /// <summary>
    /// Returns a trimmed view of <paramref name="messages"/> that fits within
    /// <paramref name="maxTokens"/>.
    /// </summary>
    /// <param name="messages">The full conversation history.</param>
    /// <param name="maxTokens">Maximum token budget for the returned list.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A list of messages that fits within the token budget. May be the same list instance
    /// when no trimming is needed, or a new trimmed list.
    /// </returns>
    Task<IList<Message>> TrimAsync(IList<Message> messages, int maxTokens, CancellationToken ct = default);
}
