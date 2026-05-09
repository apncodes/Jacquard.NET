namespace StrandsAgents.Core;

/// <summary>
/// Stateless conversation manager — no history is maintained between calls.
/// Use for single-turn agents or when you manage history externally.
/// </summary>
public sealed class NullConversationManager : IConversationManager
{
    private Message? _lastMessage;

    /// <inheritdoc/>
    public IReadOnlyList<Message> GetMessages() =>
        _lastMessage is null ? [] : [_lastMessage];

    /// <inheritdoc/>
    public void Append(Message message) => _lastMessage = message;

    /// <inheritdoc/>
    public void Trim() => _lastMessage = null;
}
