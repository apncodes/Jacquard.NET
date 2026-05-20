using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jacquard.Runtime.Session;
using Jacquard.Core;
using Xunit;

namespace Jacquard.Runtime.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task LoadAsync_UnknownSessionId_ReturnsNull()
    {
        // Fake handler returns 404
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, "");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake.agentcore/") };

        await using var manager = new AgentCoreSessionManager("mem-123", clientOverride: http);

        var result = await manager.LoadAsync("unknown-session");

        Assert.Null(result);
    }

    [Fact]
    public async Task LoadAsync_KnownSessionId_ReconstructsSession()
    {
        var messages = new List<Message>
        {
            new(Role.User, [new TextBlock("Hello")]),
            new(Role.Assistant, [new TextBlock("Hi there!")]),
        };

        var state = new Dictionary<string, object?> { ["key"] = "value" };
        var now = DateTimeOffset.UtcNow;

        var session = new AgentSession("session-1", messages, state, now);
        var responseBody = JsonSerializer.Serialize(new
        {
            sessionId = "session-1",
            messagesJson = JsonSerializer.Serialize(messages),
            stateJson = JsonSerializer.Serialize(state),
            lastUpdated = now,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var handler = new FakeHttpHandler(HttpStatusCode.OK, responseBody);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake.agentcore/") };

        await using var manager = new AgentCoreSessionManager("mem-123", clientOverride: http);

        var loaded = await manager.LoadAsync("session-1");

        Assert.NotNull(loaded);
        Assert.Equal("session-1", loaded.SessionId);
        Assert.Equal(2, loaded.Messages.Count);
    }

    [Fact]
    public async Task SaveAsync_SendsPutRequest()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://fake.agentcore/") };

        await using var manager = new AgentCoreSessionManager("mem-123", clientOverride: http);

        var session = new AgentSession(
            "session-99",
            [new Message(Role.User, [new TextBlock("test")])],
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

        await manager.SaveAsync("session-99", session);

        Assert.Equal(HttpMethod.Put, handler.LastRequest?.Method);
        Assert.Contains("session-99", handler.LastRequest?.RequestUri?.ToString() ?? "");
    }

    [Fact]
    public async Task DisposeAsync_DisposesOwnedClient()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        // No clientOverride → manager owns the client
        var manager = new AgentCoreSessionManager("mem-123");

        // Should complete without throwing
        await manager.DisposeAsync();
    }

    // Minimal fake HttpMessageHandler for testing without live HTTP
    private sealed class FakeHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
