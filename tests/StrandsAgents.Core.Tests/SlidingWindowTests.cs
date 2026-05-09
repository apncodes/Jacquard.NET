using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Property-based and unit tests for <see cref="SlidingWindowConversationManager"/>.
/// Validates: Requirements 2.9
/// </summary>
public class SlidingWindowTests
{
    // ── Property-based tests ──────────────────────────────────────────────────

    /// <summary>
    /// Window invariant: GetMessages().Count never exceeds maxMessages.
    /// Validates: Requirements 2.9
    /// </summary>
    [Property]
    public bool WindowInvariant(PositiveInt maxMessages, NonEmptyArray<string> texts)
    {
        var max = Math.Max(1, Math.Min(50, maxMessages.Get));
        var manager = new SlidingWindowConversationManager(max);
        foreach (var text in texts.Get)
            manager.Append(Message.User(text));
        return manager.GetMessages().Count <= max;
    }

    /// <summary>
    /// Most recent messages preserved: after appending N > maxMessages messages,
    /// the last message in GetMessages() is the last appended message.
    /// Validates: Requirements 2.9
    /// </summary>
    [Property]
    public bool MostRecentPreserved(PositiveInt maxMessages, NonEmptyArray<string> texts)
    {
        var max = Math.Max(1, Math.Min(20, maxMessages.Get));
        if (texts.Get.Length <= max) return true; // precondition not met, skip
        var manager = new SlidingWindowConversationManager(max);
        foreach (var text in texts.Get)
            manager.Append(Message.User(text));
        var messages = manager.GetMessages();
        var lastText = ((TextBlock)messages[^1].Content[0]).Text;
        return lastText == texts.Get[^1];
    }

    /// <summary>
    /// Count accuracy: when fewer messages than maxMessages are appended,
    /// count equals the number appended.
    /// Validates: Requirements 2.9
    /// </summary>
    [Property]
    public bool CountAccuracy(PositiveInt maxMessages, NonEmptyArray<string> texts)
    {
        var max = Math.Max(5, Math.Min(50, maxMessages.Get));
        if (texts.Get.Length > max) return true; // precondition not met, skip
        var manager = new SlidingWindowConversationManager(max);
        foreach (var text in texts.Get)
            manager.Append(Message.User(text));
        return manager.GetMessages().Count == texts.Get.Length;
    }

    /// <summary>
    /// Trim is idempotent: calling Trim() multiple times doesn't change the result.
    /// Validates: Requirements 2.9
    /// </summary>
    [Property]
    public bool TrimIsIdempotent(PositiveInt maxMessages, NonEmptyArray<string> texts)
    {
        var max = Math.Max(1, Math.Min(50, maxMessages.Get));
        var manager = new SlidingWindowConversationManager(max);
        foreach (var text in texts.Get)
            manager.Append(Message.User(text));
        var countAfterFirst = manager.GetMessages().Count;
        manager.Trim();
        var countAfterSecond = manager.GetMessages().Count;
        return countAfterFirst == countAfterSecond;
    }

    // ── Unit tests ────────────────────────────────────────────────────────────

    [Fact]
    public void AppendFewerThanWindow_AllRetained()
    {
        var manager = new SlidingWindowConversationManager(5);
        manager.Append(Message.User("a"));
        manager.Append(Message.User("b"));
        manager.Append(Message.User("c"));

        Assert.Equal(3, manager.GetMessages().Count);
    }

    [Fact]
    public void AppendExactlyMaxMessages_AllRetained()
    {
        var manager = new SlidingWindowConversationManager(3);
        manager.Append(Message.User("a"));
        manager.Append(Message.User("b"));
        manager.Append(Message.User("c"));

        Assert.Equal(3, manager.GetMessages().Count);
    }

    [Fact]
    public void AppendMoreThanMaxMessages_OnlyLastNRetained()
    {
        var manager = new SlidingWindowConversationManager(3);
        manager.Append(Message.User("a"));
        manager.Append(Message.User("b"));
        manager.Append(Message.User("c"));
        manager.Append(Message.User("d"));
        manager.Append(Message.User("e"));

        var messages = manager.GetMessages();
        Assert.Equal(3, messages.Count);
        Assert.Equal("c", ((TextBlock)messages[0].Content[0]).Text);
        Assert.Equal("d", ((TextBlock)messages[1].Content[0]).Text);
        Assert.Equal("e", ((TextBlock)messages[2].Content[0]).Text);
    }

    [Fact]
    public void Constructor_MaxMessagesZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SlidingWindowConversationManager(0));
    }
}
