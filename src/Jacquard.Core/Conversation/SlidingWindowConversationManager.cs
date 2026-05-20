namespace Jacquard.Core;

/// <summary>
/// Keeps only the most recent N messages in memory.
/// When the message count exceeds <see cref="MaxMessages"/>, the oldest messages are dropped.
/// </summary>
public sealed class SlidingWindowConversationManager : IConversationManager
{
    private readonly List<Message> _messages = [];

    /// <summary>The maximum number of messages to retain.</summary>
    public int MaxMessages { get; }

    /// <summary>
    /// Initializes a new <see cref="SlidingWindowConversationManager"/>.
    /// </summary>
    /// <param name="maxMessages">Maximum number of messages to keep. Must be >= 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxMessages"/> is less than 1.</exception>
    public SlidingWindowConversationManager(int maxMessages)
    {
        if (maxMessages < 1)
            throw new ArgumentOutOfRangeException(nameof(maxMessages), maxMessages,
                "maxMessages must be >= 1.");

        MaxMessages = maxMessages;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Message> GetMessages() => _messages.AsReadOnly();

    /// <inheritdoc/>
    public void Append(Message message)
    {
        _messages.Add(message);
        Trim();
    }

    /// <inheritdoc/>
    public void Trim()
    {
        if (_messages.Count > MaxMessages)
            _messages.RemoveRange(0, _messages.Count - MaxMessages);
    }
}
