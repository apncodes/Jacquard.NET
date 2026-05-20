namespace Jacquard.Core;

/// <summary>A single message in the conversation.</summary>
public record Message(Role Role, IReadOnlyList<ContentBlock> Content)
{
    /// <summary>Creates a simple user text message.</summary>
    public static Message User(string text) =>
        new(Role.User, [new TextBlock(text)]);

    /// <summary>Creates a simple assistant text message.</summary>
    public static Message Assistant(string text) =>
        new(Role.Assistant, [new TextBlock(text)]);
}

/// <summary>Conversation participant role.</summary>
public enum Role { User, Assistant }
