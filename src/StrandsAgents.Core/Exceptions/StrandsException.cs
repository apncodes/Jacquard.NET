namespace StrandsAgents.Core;

/// <summary>
/// Base class for all exceptions thrown by the Strands SDK.
/// Optionally carries a snapshot of the conversation history at the time of failure.
/// </summary>
public abstract class StrandsException : Exception
{
    /// <summary>
    /// The conversation messages present when the exception occurred, or <c>null</c>
    /// if the exception was thrown outside of an agent loop context.
    /// </summary>
    public IReadOnlyList<Message>? ConversationSnapshot { get; }

    /// <summary>
    /// Initializes a new <see cref="StrandsException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="conversationSnapshot">The conversation history at the time of failure, if available.</param>
    /// <param name="inner">The exception that caused this one, if any.</param>
    protected StrandsException(
        string message,
        IReadOnlyList<Message>? conversationSnapshot = null,
        Exception? inner = null)
        : base(message, inner)
    {
        ConversationSnapshot = conversationSnapshot;
    }
}
