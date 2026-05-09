namespace StrandsAgents.Core;

/// <summary>
/// Keeps full conversation history in memory. Default conversation manager.
/// History persists across InvokeAsync calls on the same Agent instance.
/// </summary>
public sealed class InMemoryConversationManager : IConversationManager
{
    private readonly List<Message> _messages = [];

    /// <inheritdoc/>
    public IReadOnlyList<Message> GetMessages() => _messages.AsReadOnly();

    /// <inheritdoc/>
    public void Append(Message message) => _messages.Add(message);

    /// <inheritdoc/>
    public void Trim() { /* No-op — full history retained */ }

    /// <summary>Clears all conversation history.</summary>
    public void Clear() => _messages.Clear();
}
