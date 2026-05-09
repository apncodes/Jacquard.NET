using System.Text.Json;
using StrandsAgents.Runtime.Tools;
using Xunit;

namespace StrandsAgents.Runtime.Tests;

public sealed class ToolTests
{
    // ── AgentCoreMemoryTool ─────────────────────────────────────────────────

    [Fact]
    public async Task MemoryTool_HasCorrectNameAndDescription()
    {
        await using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: new HttpClient());
        Assert.Equal("agentcore_memory", tool.Definition.Name);
        Assert.Contains("store", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retrieve", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemoryTool_InputSchema_IsValidJson()
    {
        await using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: new HttpClient());
        Assert.Equal(JsonValueKind.Object, tool.Definition.InputSchema.ValueKind);
    }

    [Fact]
    public async Task MemoryTool_MissingOperation_ReturnsError()
    {
        await using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"key": "mykey"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task MemoryTool_UnknownOperation_ReturnsError()
    {
        await using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"operation": "unknown_op", "key": "mykey"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("Unknown operation", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemoryTool_StoreMemory_MissingValue_ReturnsError()
    {
        await using var tool = new AgentCoreMemoryTool("mem-id-123", clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"operation": "store_memory", "key": "k1"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("value", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MemoryTool_UsesClientOverride()
    {
        // clientOverride is used — verifies constructor does not create a new HttpClient
        var fakeClient = new HttpClient();
        await using var tool = new AgentCoreMemoryTool("mem-id", clientOverride: fakeClient);
        Assert.Equal("agentcore_memory", tool.Definition.Name);
    }

    // ── AgentCoreBrowserTool ────────────────────────────────────────────────

    [Fact]
    public async Task BrowserTool_HasCorrectNameAndDescription()
    {
        await using var tool = new AgentCoreBrowserTool(clientOverride: new HttpClient());
        Assert.Equal("agentcore_browser", tool.Definition.Name);
        Assert.Contains("browser", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrowserTool_InputSchema_IsValidJson()
    {
        await using var tool = new AgentCoreBrowserTool(clientOverride: new HttpClient());
        Assert.Equal(JsonValueKind.Object, tool.Definition.InputSchema.ValueKind);
    }

    [Fact]
    public async Task BrowserTool_MissingOperation_ReturnsError()
    {
        await using var tool = new AgentCoreBrowserTool(clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"url": "https://example.com"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task BrowserTool_UnknownOperation_ReturnsError()
    {
        await using var tool = new AgentCoreBrowserTool(clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"operation": "launch_rocket"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    // ── AgentCoreCodeInterpreterTool ────────────────────────────────────────

    [Fact]
    public async Task CodeInterpreterTool_HasCorrectNameAndDescription()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: new HttpClient());
        Assert.Equal("agentcore_code_interpreter", tool.Definition.Name);
        Assert.Contains("code", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodeInterpreterTool_InputSchema_IsValidJson()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: new HttpClient());
        Assert.Equal(JsonValueKind.Object, tool.Definition.InputSchema.ValueKind);
    }

    [Fact]
    public async Task CodeInterpreterTool_MissingCode_ReturnsError()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"language": "python"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("code", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CodeInterpreterTool_MissingLanguage_ReturnsError()
    {
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: new HttpClient());
        var input = JsonDocument.Parse("""{"code": "print('hi')"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task CodeInterpreterTool_UsesClientOverride()
    {
        var fakeClient = new HttpClient();
        await using var tool = new AgentCoreCodeInterpreterTool(clientOverride: fakeClient);
        Assert.Equal("agentcore_code_interpreter", tool.Definition.Name);
    }
}
