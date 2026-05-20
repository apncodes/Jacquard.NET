using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Jacquard.Runtime.Hosting;
using Jacquard.Core;
using Xunit;

namespace Jacquard.Runtime.Tests;

public sealed class HostingTests
{
    private static HttpClient BuildTestClient(IAgent agent)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(agent);

        var app = builder.Build();
        app.MapAgentCoreEndpoints();

        app.StartAsync().GetAwaiter().GetResult();
        return app.GetTestClient();
    }

    private static AgentResult MakeResult(string message = "Hello!") =>
        new(
            Message: message,
            StopReason: StopReason.EndTurn,
            Usage: new TokenUsage(10, 20),
            Metrics: new AgentMetrics(
                TotalLatency: TimeSpan.FromMilliseconds(100),
                Iterations: 1,
                ToolCallCount: 0,
                TotalUsage: new TokenUsage(10, 20)));

    private static Mock<IAgent> BuildMockAgent(string message = "Hello!")
    {
        var result = MakeResult(message);
        var mock = new Mock<IAgent>();

        mock.Setup(a => a.InvokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        mock.Setup(a => a.StreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableOf(
                new TextDeltaEvent("Hello"),
                new TextDeltaEvent("!"),
                new AgentCompleteEvent(result)));

        return mock;
    }

    [Fact]
    public async Task GetHealth_Returns200WithHealthyStatus()
    {
        var client = BuildTestClient(BuildMockAgent().Object);
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Jacquard.NET", body);
    }

    [Fact]
    public async Task PostInvocations_NonStreaming_InvokesAgentAndReturnsJson()
    {
        var mock = BuildMockAgent("The answer is 42.");
        var client = BuildTestClient(mock.Object);

        var response = await client.PostAsJsonAsync("/invocations",
            new { prompt = "What is 6 times 7?" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("42", body);

        mock.Verify(a => a.InvokeAsync("What is 6 times 7?", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PostInvocations_Streaming_EmitsSseEvents()
    {
        var client = BuildTestClient(BuildMockAgent().Object);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/invocations")
        {
            Content = new StringContent(
                """{"prompt": "Say hello"}""",
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Accept", "text/event-stream");

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data:", body);
        Assert.Contains("[DONE]", body);
    }

    [Fact]
    public async Task PostInvocations_MissingPrompt_Returns400()
    {
        var client = BuildTestClient(BuildMockAgent().Object);

        var response = await client.PostAsJsonAsync("/invocations",
            new { sessionId = "abc" }); // no prompt

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostInvocations_InvalidJson_Returns400()
    {
        var client = BuildTestClient(BuildMockAgent().Object);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/invocations")
        {
            Content = new StringContent("not json at all", Encoding.UTF8, "application/json"),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MapAgentCoreEndpoints_RegistersBothRoutes()
    {
        var client = BuildTestClient(BuildMockAgent().Object);

        // /health responds to GET
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // /invocations only accepts POST → GET returns 405
        var wrongMethod = await client.GetAsync("/invocations");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
    }

    [Fact]
    public void UseAgentCorePort_AddsUrlToApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(BuildMockAgent().Object);

        var app = builder.Build();
        app.MapAgentCoreEndpoints();
        app.UseAgentCorePort(8080);

        Assert.Contains("http://0.0.0.0:8080", app.Urls);
    }

    // Helper — wraps a sequence of StreamEvent as IAsyncEnumerable
    private static async IAsyncEnumerable<StreamEvent> AsyncEnumerableOf(
        params StreamEvent[] events)
    {
        foreach (var e in events)
        {
            yield return e;
            await Task.Yield();
        }
    }
}
