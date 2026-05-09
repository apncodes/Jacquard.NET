using StrandsAgents.Core;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class ContextWindowStrategyTests
{
    private static Message UserMsg(string text) => Message.User(text);

    private static Message AssistantMsg(string text) =>
        new(Role.Assistant, [new TextBlock(text)]);

    // ── Token estimation sanity check ────────────────────────────────────

    [Fact]
    public async Task TrimAsync_MessagesUnderBudget_ReturnsAllMessages()
    {
        var strategy = new SlidingWindowStrategy();
        var messages = new List<Message>
        {
            UserMsg("Hello"),           // ~5 chars / 4 + 4 = ~5 tokens
            AssistantMsg("Hi there!"),  // ~9 chars / 4 + 4 = ~6 tokens
        };

        var result = await strategy.TrimAsync(messages, maxTokens: 10_000);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task TrimAsync_EmptyList_ReturnsEmpty()
    {
        var strategy = new SlidingWindowStrategy();
        var result = await strategy.TrimAsync([], maxTokens: 1000);
        Assert.Empty(result);
    }

    // ── Trimming behaviour ────────────────────────────────────────────────

    [Fact]
    public async Task TrimAsync_OverBudget_DropsMiddleMessages_KeepsMostRecent()
    {
        var strategy = new SlidingWindowStrategy();

        // Build 10 messages; each ~100 chars = 25 tokens + 4 overhead = ~29 tokens.
        // Budget of 60 tokens should keep first + last 2.
        var messages = Enumerable.Range(0, 10)
            .Select(i => UserMsg(new string('a', 100) + $" msg{i}"))
            .ToList<Message>();

        var result = await strategy.TrimAsync(messages, maxTokens: 60);

        // First message must be preserved
        Assert.Equal(messages[0], result[0]);
        // Most recent message must be preserved
        Assert.Equal(messages[^1], result[^1]);
        // Overall count is less than original
        Assert.True(result.Count < messages.Count);
    }

    [Fact]
    public async Task TrimAsync_FirstMessageAlwaysPreserved()
    {
        var strategy = new SlidingWindowStrategy();
        var messages = Enumerable.Range(0, 20)
            .Select(i => UserMsg(new string('x', 200)))
            .ToList<Message>();

        // Budget that fits only a couple of messages
        var result = await strategy.TrimAsync(messages, maxTokens: 100);

        Assert.Equal(messages[0], result[0]);
    }

    [Fact]
    public async Task TrimAsync_MostRecentMessagePreserved()
    {
        var strategy = new SlidingWindowStrategy();
        var messages = Enumerable.Range(0, 20)
            .Select(i => UserMsg(new string('x', 200)))
            .ToList<Message>();

        var result = await strategy.TrimAsync(messages, maxTokens: 200);

        Assert.Equal(messages[^1], result[^1]);
    }

    // ── EventLoop integration ─────────────────────────────────────────────

    [Fact]
    public async Task EventLoop_WithStrategy_TrimmedListPassedToModel()
    {
        // Build 5 large messages
        var bigMessages = Enumerable.Range(0, 5)
            .Select(i => Message.User(new string('a', 2000) + $" {i}"))
            .ToList();

        ModelRequest? capturedRequest = null;
        var model = new Moq.Mock<IModel>();
        model.Setup(m => m.InvokeAsync(Moq.It.IsAny<ModelRequest>(), Moq.It.IsAny<CancellationToken>()))
             .Returns<ModelRequest, CancellationToken>((r, _) =>
             {
                 capturedRequest = r;
                 return Task.FromResult(new ModelResponse("ok", [], StopReason.EndTurn, TokenUsage.Zero));
             });

        // Budget: ~100 tokens — forces trimming (2000 chars ≈ 500 tokens per message)
        var config = new AgentConfig
        {
            ContextWindowStrategy = new SlidingWindowStrategy(),
            MaxContextTokens = 100
        };

        var loop = new EventLoop(model.Object, new ToolRegistry(), config);
        await loop.RunAsync(bigMessages, null, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        // The full 5 messages were trimmed — model received fewer
        Assert.True(capturedRequest!.Messages.Count < 5);
    }
}
