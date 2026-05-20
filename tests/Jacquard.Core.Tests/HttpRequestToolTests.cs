using Moq;
using Jacquard.Tools;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Jacquard.Core.Tests;

public class HttpRequestToolTests
{
    private static IHttpClientFactory MakeFactory(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var client = new HttpClient(handler) { BaseAddress = null };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory.Object;
    }

    [Fact]
    public async Task HttpRequestTool_Get_SuccessReturnsContent()
    {
        var factory = MakeFactory(HttpStatusCode.OK, """{"temp":25}""");
        var tool = new HttpRequestTool(factory);

        var input = JsonDocument.Parse("""{"method":"GET","url":"http://example.com/weather"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Contains("200", result.Content);
        Assert.Contains("temp", result.Content);
    }

    [Fact]
    public async Task HttpRequestTool_Post_SendsBody()
    {
        string? capturedBody = null;
        var handler = new CaptureBodyHandler(HttpStatusCode.Created, "{}", b => capturedBody = b);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var tool = new HttpRequestTool(factory.Object);
        var input = JsonDocument.Parse("""{"method":"POST","url":"http://example.com/data","body":"{\"key\":\"value\"}"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.NotNull(capturedBody);
        Assert.Contains("key", capturedBody);
    }

    [Fact]
    public async Task HttpRequestTool_ServerError_ReturnsFailure()
    {
        var factory = MakeFactory(HttpStatusCode.InternalServerError, "oops");
        var tool = new HttpRequestTool(factory);

        var input = JsonDocument.Parse("""{"method":"GET","url":"http://example.com/fail"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("500", result.Content);
    }

    [Fact]
    public async Task HttpRequestTool_NetworkFailure_ReturnsFailure()
    {
        var handler = new ThrowingHandler();
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);

        var tool = new HttpRequestTool(factory.Object);
        var input = JsonDocument.Parse("""{"method":"GET","url":"http://example.com/gone"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("Request failed", result.Content);
    }

    [Fact]
    public async Task HttpRequestTool_MissingMethod_ReturnsFailure()
    {
        var factory = MakeFactory(HttpStatusCode.OK, "");
        var tool = new HttpRequestTool(factory);
        var input = JsonDocument.Parse("""{"url":"http://example.com"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("Missing required fields", result.Content);
    }

    // ── Stub handlers ─────────────────────────────────────────────────────────

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CaptureBodyHandler(
        HttpStatusCode status, string responseBody, Action<string> capture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                capture(await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody)
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }
}
