using Moq;
using StrandsAgents.Core;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class HttpClientFactoryTests
{
    // ── OpenAICompatibleModel ─────────────────────────────────────────────

    [Fact]
    public void OpenAICompatibleModel_WhenFactoryProvided_UsesFactoryClient()
    {
        var mockClient = new HttpClient(new NoOpHandler());
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory
            .Setup(f => f.CreateClient(OpenAICompatibleModel.HttpClientName))
            .Returns(mockClient);

        using var model = new OpenAICompatibleModel(
            "http://localhost", "key", httpClientFactory: mockFactory.Object);

        mockFactory.Verify(
            f => f.CreateClient(OpenAICompatibleModel.HttpClientName),
            Times.Once);
    }

    [Fact]
    public void OpenAICompatibleModel_WhenFactoryProvided_DoesNotDisposeClient()
    {
        var trackingHandler = new DisposalTrackingHandler();
        var factoryClient = new HttpClient(trackingHandler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(factoryClient);

        var model = new OpenAICompatibleModel(
            "http://localhost", "key", httpClientFactory: mockFactory.Object);

        model.Dispose();

        // Factory-managed clients must not be disposed by the consumer.
        Assert.False(trackingHandler.Disposed);
    }

    [Fact]
    public void OpenAICompatibleModel_WhenNoClientOrFactory_OwnsAndDisposesClient()
    {
        // When no client or factory is supplied, the model creates and owns an HttpClient.
        // There is no public way to inspect the internal client, but we can verify the
        // model disposes without throwing (correct ownership semantics).
        var model = new OpenAICompatibleModel("http://localhost", "key");
        model.Dispose(); // should not throw
    }

    [Fact]
    public void OpenAICompatibleModel_WhenHttpClientProvided_DoesNotOwnClient()
    {
        var trackingHandler = new DisposalTrackingHandler();
        var injectedClient = new HttpClient(trackingHandler);

        var model = new OpenAICompatibleModel(
            "http://localhost", "key", httpClient: injectedClient);

        model.Dispose();

        Assert.False(trackingHandler.Disposed);
    }

    // ── AnthropicModel ────────────────────────────────────────────────────

    [Fact]
    public void AnthropicModel_WhenFactoryProvided_UsesFactoryClient()
    {
        var mockClient = new HttpClient(new NoOpHandler());
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory
            .Setup(f => f.CreateClient(AnthropicModel.HttpClientName))
            .Returns(mockClient);

        using var model = new AnthropicModel(
            "sk-test", httpClientFactory: mockFactory.Object);

        mockFactory.Verify(
            f => f.CreateClient(AnthropicModel.HttpClientName),
            Times.Once);
    }

    [Fact]
    public void AnthropicModel_WhenFactoryProvided_DoesNotDisposeClient()
    {
        var trackingHandler = new DisposalTrackingHandler();
        var factoryClient = new HttpClient(trackingHandler);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(factoryClient);

        var model = new AnthropicModel("sk-test", httpClientFactory: mockFactory.Object);
        model.Dispose();

        Assert.False(trackingHandler.Disposed);
    }
}

// ── Test helpers ──────────────────────────────────────────────────────────

internal sealed class NoOpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
}

internal sealed class DisposalTrackingHandler : HttpMessageHandler
{
    public bool Disposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
}
