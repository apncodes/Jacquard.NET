using Moq;
using Jacquard.Core;
using System.Text.Json;
using Xunit;

namespace Jacquard.Core.Tests;

public class HookRegistryTests
{
    private static ModelRequest FakeRequest() =>
        new([], null, [], new ModelParameters());

    private static ModelResponse FakeResponse() =>
        new("ok", [], StopReason.EndTurn, TokenUsage.Zero);

    // 1. Dispatch order — FIFO
    [Fact]
    public async Task FireAsync_MultipleHandlers_InvokedInFifoOrder()
    {
        var registry = new HookRegistry();
        var order = new List<string>();

        registry.Register<BeforeModelCallEvent>(_ => { order.Add("A"); return Task.CompletedTask; });
        registry.Register<BeforeModelCallEvent>(_ => { order.Add("B"); return Task.CompletedTask; });

        await registry.FireAsync(new BeforeModelCallEvent(FakeRequest()));

        Assert.Equal(["A", "B"], order);
    }

    // 2. Interrupt flag — visible to caller after FireAsync
    [Fact]
    public async Task FireAsync_HandlerSetsInterrupt_FlagVisibleAfterReturn()
    {
        var registry = new HookRegistry();
        registry.Register<BeforeModelCallEvent>(evt =>
        {
            evt.Interrupt = true;
            return Task.CompletedTask;
        });

        var hookEvent = new BeforeModelCallEvent(FakeRequest());
        await registry.FireAsync(hookEvent);

        Assert.True(hookEvent.Interrupt);
    }

    // 3. Exception propagation
    [Fact]
    public async Task FireAsync_HandlerThrows_ExceptionPropagates()
    {
        var registry = new HookRegistry();
        registry.Register<BeforeModelCallEvent>(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.FireAsync(new BeforeModelCallEvent(FakeRequest())));
    }

    // 4. No handlers — completes without error
    [Fact]
    public async Task FireAsync_NoHandlers_CompletesNormally()
    {
        var registry = new HookRegistry();
        // Should not throw
        await registry.FireAsync(new BeforeModelCallEvent(FakeRequest()));
    }

    // 5. Different event types — handler not invoked for wrong type
    [Fact]
    public async Task FireAsync_DifferentEventType_HandlerNotInvoked()
    {
        var registry = new HookRegistry();
        var called = false;
        registry.Register<BeforeModelCallEvent>(_ => { called = true; return Task.CompletedTask; });

        var req = FakeRequest();
        await registry.FireAsync(new AfterModelCallEvent(req, FakeResponse()));

        Assert.False(called);
    }

    // 6. CancellationToken — cancelled before FireAsync throws OperationCanceledException
    [Fact]
    public async Task FireAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var registry = new HookRegistry();
        registry.Register<BeforeModelCallEvent>(_ => Task.CompletedTask);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => registry.FireAsync(new BeforeModelCallEvent(FakeRequest()), cts.Token));
    }

    // 7. EventLoop integration — BeforeModelCallEvent interrupt halts loop before model call
    [Fact]
    public async Task EventLoop_BeforeModelCallInterrupt_ModelNotCalled()
    {
        var model = new Mock<IModel>();
        model.Setup(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(FakeResponse());

        var hooks = new HookRegistry();
        hooks.Register<BeforeModelCallEvent>(evt =>
        {
            evt.Interrupt = true;
            return Task.CompletedTask;
        });

        var loop = new EventLoop(model.Object, new ToolRegistry(), new AgentConfig(), hooks);
        await loop.RunAsync([Message.User("Hi")], null, CancellationToken.None);

        model.Verify(m => m.InvokeAsync(It.IsAny<ModelRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
