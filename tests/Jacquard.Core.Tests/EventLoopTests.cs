using Moq;
using Jacquard.Core;
using System.Text.Json;
using Xunit;

namespace Jacquard.Core.Tests;

public class EventLoopTests
{
    private static ToolRegistry EmptyRegistry() => new();

    private static ToolRegistry RegistryWith(params ITool[] tools)
    {
        var r = new ToolRegistry();
        r.RegisterAll(tools);
        return r;
    }

    [Fact]
    public async Task RunAsync_EndTurn_ReturnsResult()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ModelResponse("Hello!", [], StopReason.EndTurn, TokenUsage.Zero));

        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig());
        var (result, _) = await loop.RunAsync([Message.User("Hi")], null, CancellationToken.None);

        Assert.Equal("Hello!", result.Message);
        Assert.Equal(StopReason.EndTurn, result.StopReason);
    }

    [Fact]
    public async Task RunAsync_ToolUse_ExecutesToolAndLoops()
    {
        var toolCall = new ToolCall("id1", "add", JsonDocument.Parse("{\"a\":1,\"b\":2}").RootElement);
        var fakeTool = new FakeTool("add", ToolResult.Success("id1", "3"));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? new ModelResponse(null, [toolCall], StopReason.ToolUse, TokenUsage.Zero)
                     : new ModelResponse("Result: 3", [], StopReason.EndTurn, TokenUsage.Zero);
             });

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig());
        var (result, _) = await loop.RunAsync([Message.User("Add 1+2")], null, CancellationToken.None);

        Assert.Equal("Result: 3", result.Message);
        Assert.Equal(StopReason.EndTurn, result.StopReason);
        Assert.Equal(1, fakeTool.InvokeCount);
    }

    [Fact]
    public async Task RunAsync_MaxIterations_StopsWithMaxIterationsReason()
    {
        var toolCall = new ToolCall("id1", "loop", JsonDocument.Parse("{}").RootElement);
        var fakeTool = new FakeTool("loop", ToolResult.Success("id1", "ok"));

        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new ModelResponse(null, [toolCall], StopReason.ToolUse, TokenUsage.Zero));

        var loop = new EventLoop(model.Object, RegistryWith(fakeTool), new AgentConfig { MaxIterations = 3 });
        var (result, _) = await loop.RunAsync([Message.User("Loop")], null, CancellationToken.None);

        Assert.Equal(StopReason.MaxIterations, result.StopReason);
    }

    [Fact]
    public async Task RunAsync_CancellationToken_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var model = new Mock<IModel>();
        var loop = new EventLoop(model.Object, EmptyRegistry(), new AgentConfig());

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => loop.RunAsync([Message.User("Hi")], null, cts.Token));
    }

    [Fact]
    public async Task RunAsync_ParallelToolExecution_ExecutesBothTools()
    {
        var call1 = new ToolCall("id1", "tool1", JsonDocument.Parse("{}").RootElement);
        var call2 = new ToolCall("id2", "tool2", JsonDocument.Parse("{}").RootElement);
        var fake1 = new FakeTool("tool1", ToolResult.Success("id1", "a"));
        var fake2 = new FakeTool("tool2", ToolResult.Success("id2", "b"));

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? new ModelResponse(null, [call1, call2], StopReason.ToolUse, TokenUsage.Zero)
                     : new ModelResponse("Done", [], StopReason.EndTurn, TokenUsage.Zero);
             });

        var loop = new EventLoop(model.Object, RegistryWith(fake1, fake2), new AgentConfig { ParallelToolExecution = true });
        var (result, _) = await loop.RunAsync([Message.User("Run both")], null, CancellationToken.None);

        Assert.Equal(StopReason.EndTurn, result.StopReason);
        Assert.Equal(1, fake1.InvokeCount);
        Assert.Equal(1, fake2.InvokeCount);
    }

    [Fact]
    public async Task RunAsync_ToolThrows_ContinuesWithErrorResult()
    {
        var toolCall = new ToolCall("id1", "boom", JsonDocument.Parse("{}").RootElement);
        var throwingTool = new ThrowingTool("boom");

        var callCount = 0;
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(() =>
             {
                 callCount++;
                 return callCount == 1
                     ? new ModelResponse(null, [toolCall], StopReason.ToolUse, TokenUsage.Zero)
                     : new ModelResponse("Handled error", [], StopReason.EndTurn, TokenUsage.Zero);
             });

        var loop = new EventLoop(model.Object, RegistryWith(throwingTool), new AgentConfig());
        var (result, messages) = await loop.RunAsync([Message.User("Boom")], null, CancellationToken.None);

        // Loop should continue after tool error
        Assert.Equal(StopReason.EndTurn, result.StopReason);
        // Tool result message should contain error
        var toolResultMsg = messages.FirstOrDefault(m => m.Content.OfType<ToolResultBlock>().Any());
        Assert.NotNull(toolResultMsg);
        var toolResult = toolResultMsg!.Content.OfType<ToolResultBlock>().First();
        Assert.True(toolResult.IsError);
    }
}

// Test helpers
internal sealed class FakeTool : ITool
{
    private readonly ToolResult _result;
    public int InvokeCount { get; private set; }

    public FakeTool(string name, ToolResult result)
    {
        _result = result;
        Definition = new ToolDefinition(name, "test tool", JsonDocument.Parse("{}").RootElement);
    }

    public ToolDefinition Definition { get; }

    public Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        InvokeCount++;
        return Task.FromResult(_result);
    }
}

internal sealed class ThrowingTool : ITool
{
    public ThrowingTool(string name)
    {
        Definition = new ToolDefinition(name, "throws", JsonDocument.Parse("{}").RootElement);
    }

    public ToolDefinition Definition { get; }

    public Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
        => throw new InvalidOperationException("Tool exploded");
}
