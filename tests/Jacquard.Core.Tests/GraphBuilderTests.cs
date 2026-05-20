using Moq;
using Jacquard.Core;
using Jacquard.MultiAgent;
using Xunit;

namespace Jacquard.Core.Tests;

public class GraphBuilderTests
{
    private static Mock<IAgent> AgentReturning(string message)
    {
        var mock = new Mock<IAgent>();
        mock.Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResult(message, StopReason.EndTurn, TokenUsage.Zero,
                new AgentMetrics(TimeSpan.Zero, 1, 0, TokenUsage.Zero)));
        return mock;
    }

    // 1. Single node — graph with one node, no edges
    [Fact]
    public async Task SingleNode_NoEdges_InvokesNodeAndReturnsResult()
    {
        var agent = AgentReturning("hello");
        var graph = new GraphBuilder()
            .AddNode("A", agent.Object)
            .Build();

        var result = await graph.RunAsync("input");

        Assert.Equal("hello", result.Message);
        agent.Verify(a => a.InvokeAsync("input", It.IsAny<CancellationToken>()), Times.Once);
    }

    // 2. Linear chain — A → B → C, each receives previous node's output
    [Fact]
    public async Task LinearChain_NodesExecuteInOrder_EachReceivesPreviousOutput()
    {
        var receivedPrompts = new List<string>();

        Mock<IAgent> MakeCapturingAgent(string returnMessage)
        {
            var mock = new Mock<IAgent>();
            mock.Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((p, _) => receivedPrompts.Add(p))
                .ReturnsAsync(new AgentResult(returnMessage, StopReason.EndTurn, TokenUsage.Zero,
                    new AgentMetrics(TimeSpan.Zero, 1, 0, TokenUsage.Zero)));
            return mock;
        }

        var agentA = MakeCapturingAgent("output-A");
        var agentB = MakeCapturingAgent("output-B");
        var agentC = MakeCapturingAgent("output-C");

        var graph = new GraphBuilder()
            .AddNode("A", agentA.Object)
            .AddNode("B", agentB.Object)
            .AddNode("C", agentC.Object)
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .Build();

        var result = await graph.RunAsync("initial");

        Assert.Equal("output-C", result.Message);
        Assert.Equal(3, receivedPrompts.Count);
        Assert.Equal("initial", receivedPrompts[0]);
        Assert.Equal("output-A", receivedPrompts[1]);
        Assert.Equal("output-B", receivedPrompts[2]);
    }

    // 3. Conditional edge returns End → execution stops
    [Fact]
    public async Task ConditionalEdge_ReturnsEnd_StopsExecution()
    {
        var agentA = AgentReturning("result-A");
        var agentB = AgentReturning("result-B");

        var graph = new GraphBuilder()
            .AddNode("A", agentA.Object)
            .AddNode("B", agentB.Object)
            .AddConditionalEdge("A", _ => GraphBuilder.End)
            .Build();

        var result = await graph.RunAsync("prompt");

        Assert.Equal("result-A", result.Message);
        agentB.Verify(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 4. Conditional edge routes to different nodes based on result
    [Fact]
    public async Task ConditionalEdge_RoutesToCorrectNode_BasedOnResult()
    {
        var agentA = AgentReturning("go-to-B");
        var agentB = AgentReturning("final-B");
        var agentC = AgentReturning("final-C");

        var graph = new GraphBuilder()
            .AddNode("A", agentA.Object)
            .AddNode("B", agentB.Object)
            .AddNode("C", agentC.Object)
            .AddConditionalEdge("A", r => r.Message == "go-to-B" ? "B" : "C")
            .Build();

        var result = await graph.RunAsync("prompt");

        Assert.Equal("final-B", result.Message);
        agentB.Verify(a => a.InvokeAsync("go-to-B", It.IsAny<CancellationToken>()), Times.Once);
        agentC.Verify(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // 4b. Conditional routing — counter-based, routes differently on second call
    [Fact]
    public async Task ConditionalEdge_RoutesBasedOnCallCount()
    {
        var callCount = 0;
        var agentA = new Mock<IAgent>();
        agentA.Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                var msg = callCount == 1 ? "first" : "second";
                return new AgentResult(msg, StopReason.EndTurn, TokenUsage.Zero,
                    new AgentMetrics(TimeSpan.Zero, 1, 0, TokenUsage.Zero));
            });

        var agentB = AgentReturning("from-B");
        var agentC = AgentReturning("from-C");

        var graph = new GraphBuilder()
            .AddNode("A", agentA.Object)
            .AddNode("B", agentB.Object)
            .AddNode("C", agentC.Object)
            .AddConditionalEdge("A", r => r.Message == "first" ? "B" : "C")
            .Build();

        var result = await graph.RunAsync("prompt");

        Assert.Equal("from-B", result.Message);
    }

    // 5. No outgoing edge → execution stops after that node
    [Fact]
    public async Task NoOutgoingEdge_StopsAfterNode()
    {
        var agentA = AgentReturning("only-result");

        var graph = new GraphBuilder()
            .AddNode("A", agentA.Object)
            .Build();

        var result = await graph.RunAsync("prompt");

        Assert.Equal("only-result", result.Message);
        agentA.Verify(a => a.InvokeAsync("prompt", It.IsAny<CancellationToken>()), Times.Once);
    }

    // 6. Build validation — no nodes → throws InvalidOperationException
    [Fact]
    public void Build_NoNodes_ThrowsInvalidOperationException()
    {
        var builder = new GraphBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    // 7. Build validation — duplicate node name → throws ArgumentException
    [Fact]
    public void AddNode_DuplicateName_ThrowsArgumentException()
    {
        var agent = AgentReturning("x");
        var builder = new GraphBuilder().AddNode("A", agent.Object);

        Assert.Throws<ArgumentException>(() => builder.AddNode("A", agent.Object));
    }

    // 8. Build validation — edge to unregistered node → throws InvalidOperationException
    [Fact]
    public void Build_EdgeToUnregisteredNode_ThrowsInvalidOperationException()
    {
        var agent = AgentReturning("x");
        var builder = new GraphBuilder()
            .AddNode("A", agent.Object)
            .AddEdge("A", "NonExistent");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    // 9. Cancellation — cancelled token propagates OperationCanceledException
    [Fact]
    public async Task RunAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var agent = AgentReturning("ok");
        var graph = new GraphBuilder()
            .AddNode("A", agent.Object)
            .Build();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => graph.RunAsync("prompt", cts.Token));
    }

    // 10. Cycle guard — A → B → A cycle → throws InvalidOperationException after max iterations
    [Fact]
    public async Task CycleGuard_ExceedsMaxIterations_ThrowsInvalidOperationException()
    {
        var agentA = AgentReturning("from-A");
        var agentB = AgentReturning("from-B");

        var graph = new GraphBuilder()
            .AddNode("A", agentA.Object)
            .AddNode("B", agentB.Object)
            .AddEdge("A", "B")
            .AddEdge("B", "A")
            .Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => graph.RunAsync("prompt"));
    }
}
