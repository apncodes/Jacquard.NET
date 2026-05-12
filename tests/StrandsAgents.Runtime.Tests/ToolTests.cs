using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Moq;
using StrandsAgents.Runtime.Tools;
using Xunit;

namespace StrandsAgents.Runtime.Tests;

public sealed class ToolTests
{
    // ── AgentCoreMemoryTool ─────────────────────────────────────────────────

    private static Mock<IAmazonBedrockAgentCore> MemoryMock() => new();

    [Fact]
    public void MemoryTool_HasCorrectNameAndDescription()
    {
        using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: MemoryMock().Object);
        Assert.Equal("agentcore_memory", tool.Definition.Name);
        Assert.Contains("store", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retrieve", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemoryTool_InputSchema_IsValidJson()
    {
        using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: MemoryMock().Object);
        Assert.Equal(JsonValueKind.Object, tool.Definition.InputSchema.ValueKind);
    }

    [Fact]
    public async Task MemoryTool_MissingOperation_ReturnsError()
    {
        using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: MemoryMock().Object);
        var input = JsonDocument.Parse("""{"content": "hello"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task MemoryTool_UnknownOperation_ReturnsError()
    {
        using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: MemoryMock().Object);
        var input = JsonDocument.Parse("""{"operation": "unknown_op"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("Unknown operation", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemoryTool_StoreMemory_MissingContent_ReturnsError()
    {
        using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: MemoryMock().Object);
        var input = JsonDocument.Parse("""{"operation": "store_memory"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("content", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemoryTool_UsesClientOverride()
    {
        var mock = MemoryMock();
        using var tool = new AgentCoreMemoryTool("mem-id", clientOverride: mock.Object);
        Assert.Equal("agentcore_memory", tool.Definition.Name);
    }

    // ── AgentCoreBrowserTool ────────────────────────────────────────────────

    private static Mock<IAmazonBedrockAgentCore> BrowserMock() => new();

    [Fact]
    public void BrowserTool_HasCorrectNameAndDescription()
    {
        using var tool = new AgentCoreBrowserTool(clientOverride: BrowserMock().Object);
        Assert.Equal("agentcore_browser", tool.Definition.Name);
        Assert.Contains("browser", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BrowserTool_InputSchema_IsValidJson()
    {
        using var tool = new AgentCoreBrowserTool(clientOverride: BrowserMock().Object);
        Assert.Equal(JsonValueKind.Object, tool.Definition.InputSchema.ValueKind);
    }

    [Fact]
    public async Task BrowserTool_MissingOperation_ReturnsError()
    {
        using var tool = new AgentCoreBrowserTool(clientOverride: BrowserMock().Object);
        var input = JsonDocument.Parse("""{"session_id": "abc"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task BrowserTool_UnknownOperation_ReturnsError()
    {
        using var tool = new AgentCoreBrowserTool(clientOverride: BrowserMock().Object);
        var input = JsonDocument.Parse("""{"operation": "launch_rocket"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("launch_rocket", result.Content);
    }

    [Fact]
    public async Task BrowserTool_GetSession_MissingSessionId_ReturnsError()
    {
        using var tool = new AgentCoreBrowserTool(clientOverride: BrowserMock().Object);
        var input = JsonDocument.Parse("""{"operation": "get_session"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("session_id", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowserTool_StopSession_MissingSessionId_ReturnsError()
    {
        using var tool = new AgentCoreBrowserTool(clientOverride: BrowserMock().Object);
        var input = JsonDocument.Parse("""{"operation": "stop_session"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("session_id", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowserTool_StartSession_CallsSdkWithCorrectParameters()
    {
        var mock = BrowserMock();
        mock.Setup(c => c.StartBrowserSessionAsync(It.IsAny<StartBrowserSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StartBrowserSessionResponse
            {
                SessionId = "sessAbc123",
                Streams = new BrowserSessionStream
                {
                    AutomationStream = new AutomationStream { StreamEndpoint = "wss://example.com/stream" },
                },
            });

        using var tool = new AgentCoreBrowserTool(clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "start_session"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Contains("sessAbc123", result.Content);
        Assert.Contains("wss://example.com/stream", result.Content);
        mock.Verify(c => c.StartBrowserSessionAsync(
            It.Is<StartBrowserSessionRequest>(r => r.BrowserIdentifier == "default"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── AgentCoreCodeInterpreterTool ────────────────────────────────────────

    private static Mock<IAmazonBedrockAgentCore> CodeMock() => new();

    [Fact]
    public async Task CodeInterpreterTool_HasCorrectNameAndDescription()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: CodeMock().Object);
        Assert.Equal("agentcore_code_interpreter", tool.Definition.Name);
        Assert.Contains("code", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodeInterpreterTool_InputSchema_IsValidJson()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: CodeMock().Object);
        Assert.Equal(JsonValueKind.Object, tool.Definition.InputSchema.ValueKind);
    }

    [Fact]
    public async Task CodeInterpreterTool_MissingCode_ReturnsError()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: CodeMock().Object);
        var input = JsonDocument.Parse("""{"language": "python"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("code", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodeInterpreterTool_MissingLanguage_ReturnsError()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: CodeMock().Object);
        var input = JsonDocument.Parse("""{"code": "print('hi')"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task CodeInterpreterTool_UnsupportedLanguage_ReturnsError()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: CodeMock().Object);
        var input = JsonDocument.Parse("""{"code": "echo hi", "language": "bash"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("bash", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodeInterpreterTool_UsesClientOverride()
    {
        var mock = CodeMock();
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: mock.Object);
        Assert.Equal("agentcore_code_interpreter", tool.Definition.Name);
    }
}
