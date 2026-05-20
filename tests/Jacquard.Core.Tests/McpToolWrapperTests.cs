using ModelContextProtocol.Protocol;
using Jacquard.Core;
using System.Text.Json;
using Xunit;

namespace Jacquard.Core.Tests;

public class McpToolWrapperTests
{
    // ── helpers ───────────────────────────────────────────────────────────────

    private static Jacquard.Tools.Mcp.McpToolWrapper MakeWrapper(
        string name,
        string description,
        Func<Task<CallToolResult>> respond)
    {
        var schema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
        return new Jacquard.Tools.Mcp.McpToolWrapper(
            name,
            description,
            schema,
            (_, _, _) => respond());
    }

    private static CallToolResult OkResponse(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = false
    };

    private static CallToolResult ErrResponse(string text) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = true
    };

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void McpToolWrapper_Definition_MapsNameAndDescription()
    {
        var wrapper = MakeWrapper("weather", "Get weather", () => Task.FromResult(OkResponse("")));

        Assert.Equal("weather", wrapper.Definition.Name);
        Assert.Equal("Get weather", wrapper.Definition.Description);
    }

    [Fact]
    public async Task McpToolWrapper_InvokeAsync_PassesNameToDelegate()
    {
        string? capturedName = null;
        var schema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
        var wrapper = new Jacquard.Tools.Mcp.McpToolWrapper(
            "weather", "desc", schema,
            (n, _, _) => { capturedName = n; return Task.FromResult(OkResponse("Sunny")); });

        await wrapper.InvokeAsync(JsonDocument.Parse("""{"city":"London"}""").RootElement);

        Assert.Equal("weather", capturedName);
    }

    [Fact]
    public async Task McpToolWrapper_InvokeAsync_SuccessResponse_IsNotError()
    {
        var wrapper = MakeWrapper("weather", "desc", () => Task.FromResult(OkResponse("Sunny, 25°C")));
        var result = await wrapper.InvokeAsync(JsonDocument.Parse("""{"city":"London"}""").RootElement);

        Assert.False(result.IsError);
        Assert.Equal("Sunny, 25°C", result.Content);
    }

    [Fact]
    public async Task McpToolWrapper_InvokeAsync_WhenServerReturnsError_ResultIsError()
    {
        var wrapper = MakeWrapper("weather", "desc", () => Task.FromResult(ErrResponse("City not found")));
        var result = await wrapper.InvokeAsync(JsonDocument.Parse("{}").RootElement);

        Assert.True(result.IsError);
        Assert.Equal("City not found", result.Content);
    }

    [Fact]
    public async Task McpToolWrapper_InvokeAsync_WhenDelegateThrows_ReturnsFailure()
    {
        var schema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
        var wrapper = new Jacquard.Tools.Mcp.McpToolWrapper(
            "weather", "desc", schema,
            (_, _, _) => throw new InvalidOperationException("connection lost"));

        var result = await wrapper.InvokeAsync(JsonDocument.Parse("{}").RootElement);

        Assert.True(result.IsError);
        Assert.Contains("connection lost", result.Content);
    }
}
