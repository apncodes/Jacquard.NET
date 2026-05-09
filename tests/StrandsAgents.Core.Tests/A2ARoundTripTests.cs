using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Moq;
using StrandsAgents.Core;
using StrandsAgents.MultiAgent;
using Xunit;

namespace StrandsAgents.Core.Tests;

/// <summary>
/// Integration tests for the A2A round-trip: in-process ASP.NET Core server + A2AAgent client.
/// Uses Microsoft.AspNetCore.TestHost so no real TCP port is needed.
/// </summary>
public sealed class A2ARoundTripTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _testClient = null!;
    private Mock<IAgent> _mockAgent = null!;

    public async Task InitializeAsync()
    {
        _mockAgent = new Mock<IAgent>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.MapA2AEndpoint("/a2a", _mockAgent.Object);

        await _app.StartAsync();
        _testClient = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _testClient.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static AgentResult MakeResult(string message, StopReason stopReason = StopReason.EndTurn,
        int inputTokens = 0, int outputTokens = 0)
        => new(message, stopReason,
            new TokenUsage(inputTokens, outputTokens),
            new AgentMetrics(TimeSpan.Zero, 1, 0, new TokenUsage(inputTokens, outputTokens)));

    // 1. Basic round-trip — mock agent returns "Hello back", A2AAgent receives it
    [Fact]
    public async Task BasicRoundTrip_MockAgentReturnsMessage_A2AAgentReceivesIt()
    {
        _mockAgent
            .Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult("Hello back"));

        var a2aAgent = new A2AAgent(new Uri("http://localhost/a2a"), _testClient);
        var result = await a2aAgent.InvokeAsync("hello");

        Assert.Equal("Hello back", result.Message);
    }

    // 2. Prompt forwarded — mock agent receives the exact prompt sent by A2AAgent
    [Fact]
    public async Task PromptForwarded_MockAgentReceivesExactPrompt()
    {
        string? capturedPrompt = null;
        _mockAgent
            .Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => capturedPrompt = p)
            .ReturnsAsync(MakeResult("ok"));

        var a2aAgent = new A2AAgent(new Uri("http://localhost/a2a"), _testClient);
        await a2aAgent.InvokeAsync("the exact prompt");

        Assert.Equal("the exact prompt", capturedPrompt);
    }

    // 3. StopReason preserved — mock returns EndTurn, A2AAgent result has EndTurn
    [Fact]
    public async Task StopReasonPreserved_EndTurn_RoundTripsCorrectly()
    {
        _mockAgent
            .Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult("done", StopReason.EndTurn));

        var a2aAgent = new A2AAgent(new Uri("http://localhost/a2a"), _testClient);
        var result = await a2aAgent.InvokeAsync("prompt");

        Assert.Equal(StopReason.EndTurn, result.StopReason);
    }

    // 4. Token usage preserved — mock returns TokenUsage(10, 20), A2AAgent result matches
    [Fact]
    public async Task TokenUsagePreserved_RoundTripsCorrectly()
    {
        _mockAgent
            .Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeResult("response", inputTokens: 10, outputTokens: 20));

        var a2aAgent = new A2AAgent(new Uri("http://localhost/a2a"), _testClient);
        var result = await a2aAgent.InvokeAsync("prompt");

        Assert.Equal(10, result.Usage.InputTokens);
        Assert.Equal(20, result.Usage.OutputTokens);
    }
}
