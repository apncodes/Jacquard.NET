namespace StrandsAgents.Core;

/// <summary>Manages conversation history strategy (full, sliding window, summarizing).</summary>
public interface IConversationManager
{
    IReadOnlyList<Message> GetMessages();
    void Append(Message message);
    void Trim();
}
