using Moq;
using StrandsAgents.Core;
using StrandsAgents.MultiAgent;
using System.Text.Json;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class MultiAgentTests
{
    private static Mock<IAgent> AgentReturning(string message)
    {
        var mock = new Mock<IAgent>();
        mock.Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResult(
                message,
                StopReason.EndTurn,
                TokenUsage.Zero,
                new AgentMetrics(TimeSpan.Zero, 1, 0, TokenUsage.Zero)));
        return mock;
    }

    // ── PipelineOrchestrator ──────────────────────────────────────────────────

    [Fact]
    public async Task Pipeline_ChainsTwoStages_SecondReceivesFirstOutput()
    {
        string? secondInput = null;
        var stage1 = AgentReturning("stage1-output");
        var stage2 = new Mock<IAgent>();
        stage2.Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Callback<string, CancellationToken>((p, _) => secondInput = p)
              .ReturnsAsync(new AgentResult(
                  "final",
                  StopReason.EndTurn,
                  TokenUsage.Zero,
                  new AgentMetrics(TimeSpan.Zero, 1, 0, TokenUsage.Zero)));

        var pipeline = new PipelineOrchestrator((IEnumerable<IAgent>)[stage1.Object, stage2.Object]);
        var result = await pipeline.RunAsync("initial");

        Assert.Equal("stage1-output", secondInput);
        Assert.Equal("final", result.Message);
    }

    [Fact]
    public async Task Pipeline_SingleStage_ReturnsThatStagesResult()
    {
        var stage = AgentReturning("only-output");
        var pipeline = new PipelineOrchestrator((IEnumerable<IAgent>)[stage.Object]);
        var result = await pipeline.RunAsync("prompt");

        Assert.Equal("only-output", result.Message);
    }

    [Fact]
    public void Pipeline_EmptyStages_Throws()
    {
        Assert.Throws<ArgumentException>(() => new PipelineOrchestrator((IEnumerable<IAgent>)[]));
    }

    [Fact]
    public async Task Pipeline_CancellationPropagates()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var stage = AgentReturning("ok");
        var pipeline = new PipelineOrchestrator((IEnumerable<IAgent>)[stage.Object]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pipeline.RunAsync("prompt", cts.Token));
    }

    // ── ParallelOrchestrator ──────────────────────────────────────────────────

    [Fact]
    public async Task Parallel_AllAgentsReceiveSamePrompt()
    {
        var prompts = new List<string>();
        var agent1 = new Mock<IAgent>();
        var agent2 = new Mock<IAgent>();

        foreach (var mock in new[] { agent1, agent2 })
        {
            mock.Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback<string, CancellationToken>((p, _) => { lock (prompts) prompts.Add(p); })
                .ReturnsAsync(new AgentResult(
                    "ok",
                    StopReason.EndTurn,
                    TokenUsage.Zero,
                    new AgentMetrics(TimeSpan.Zero, 1, 0, TokenUsage.Zero)));
        }

        var orchestrator = new ParallelOrchestrator([agent1.Object, agent2.Object]);
        var results = await orchestrator.RunAsync("shared-prompt");

        Assert.Equal(2, results.Count);
        Assert.All(prompts, p => Assert.Equal("shared-prompt", p));
    }

    [Fact]
    public async Task Parallel_ReturnsAllResults()
    {
        var orchestrator = new ParallelOrchestrator(
        [
            AgentReturning("result-a").Object,
            AgentReturning("result-b").Object,
            AgentReturning("result-c").Object
        ]);

        var results = await orchestrator.RunAsync("prompt");

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Message == "result-a");
        Assert.Contains(results, r => r.Message == "result-b");
        Assert.Contains(results, r => r.Message == "result-c");
    }

    [Fact]
    public void Parallel_EmptyAgents_Throws()
    {
        Assert.Throws<ArgumentException>(() => new ParallelOrchestrator((IEnumerable<IAgent>)[]));
    }

    // ── AgentTool ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AgentTool_InvokeAsync_CallsAgentWithPrompt()
    {
        string? receivedPrompt = null;
        var agent = new Mock<IAgent>();
        agent.Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Callback<string, CancellationToken>((p, _) => receivedPrompt = p)
             .ReturnsAsync(new AgentResult(
                 "agent-answer",
                 StopReason.EndTurn,
                 TokenUsage.Zero,
                 new AgentMetrics(TimeSpan.Zero, 1, 0, TokenUsage.Zero)));

        var tool = new StrandsAgents.MultiAgent.AgentTool(agent.Object, "sub_agent", "A helpful sub-agent.");
        var input = JsonDocument.Parse("""{"prompt":"do something"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Equal("agent-answer", result.Content);
        Assert.Equal("do something", receivedPrompt);
    }

    [Fact]
    public async Task AgentTool_MissingPrompt_ReturnsFailure()
    {
        var agent = AgentReturning("ok");
        var tool = new StrandsAgents.MultiAgent.AgentTool(agent.Object, "sub_agent", "desc");
        var input = JsonDocument.Parse("{}").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("prompt", result.Content);
    }

    [Fact]
    public void AgentTool_Definition_ExposesNameAndDescription()
    {
        var tool = new StrandsAgents.MultiAgent.AgentTool(AgentReturning("x").Object, "my_tool", "Does things");
        Assert.Equal("my_tool", tool.Definition.Name);
        Assert.Equal("Does things", tool.Definition.Description);
    }
}
