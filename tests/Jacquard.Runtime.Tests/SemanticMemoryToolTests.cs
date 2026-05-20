using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Moq;
using Jacquard.Runtime.Tools;
using Xunit;

namespace Jacquard.Runtime.Tests;

/// <summary>
/// Unit tests for SemanticMemoryTool — all SDK calls are intercepted by a Moq mock.
/// </summary>
public sealed class SemanticMemoryToolTests
{
    // ── Definition ────────────────────────────────────────────────────────────

    [Fact]
    public void Definition_HasCorrectName()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        Assert.Equal("agentcore_semantic_memory", tool.Definition.Name);
    }

    [Fact]
    public void Definition_InputSchema_IsValidJsonObject()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        Assert.Equal(JsonValueKind.Object, tool.Definition.InputSchema.ValueKind);
    }

    [Fact]
    public void Definition_Description_MentionsSearchStoreDelete()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        Assert.Contains("search", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("store", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("delete", tool.Definition.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Definition_ReflectsConfiguredDefaultTopK()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id",
            options: new SemanticMemoryOptions { DefaultTopK = 20 },
            clientOverride: mock.Object);

        Assert.Contains("20", tool.Definition.Description);
        Assert.Contains("20", tool.Definition.InputSchema.GetRawText());
    }

    [Fact]
    public void Definition_ReflectsConfiguredDefaultNamespace()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id",
            options: new SemanticMemoryOptions { DefaultNamespace = "user:alex" },
            clientOverride: mock.Object);

        Assert.Contains("user:alex", tool.Definition.Description);
    }

    // ── SemanticMemoryOptions validation ─────────────────────────────────────

    [Fact]
    public void Options_DefaultTopKZero_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemanticMemoryOptions { DefaultTopK = 0 });
    }

    [Fact]
    public void Options_DefaultTopK101_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SemanticMemoryOptions { DefaultTopK = 101 });
    }

    [Fact]
    public void Options_DefaultTopK100_IsValid()
    {
        var opts = new SemanticMemoryOptions { DefaultTopK = 100 };
        Assert.Equal(100, opts.DefaultTopK);
    }

    // ── Input validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_MissingOperation_ReturnsFailure()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"query": "something"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("operation", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_UnknownOperation_ReturnsFailure()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "fly_to_moon"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("fly_to_moon", result.Content);
    }

    // ── search_memory validation ──────────────────────────────────────────────

    [Fact]
    public async Task SearchMemory_EmptyQuery_ReturnsFailureWithoutSdkCall()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "", "namespace": "user:profile"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("non-empty", result.Content, StringComparison.OrdinalIgnoreCase);
        mock.Verify(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchMemory_MissingNamespace_ReturnsFailureWithoutSdkCall()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "coffee"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("namespace", result.Content, StringComparison.OrdinalIgnoreCase);
        mock.Verify(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchMemory_TopKBelowMin_ReturnsFailureWithoutSdkCall()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "namespace": "ns", "top_k": 0}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        mock.Verify(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SearchMemory_TopKAboveMax_ReturnsFailureWithoutSdkCall()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "namespace": "ns", "top_k": 101}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        mock.Verify(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── search_memory SDK call ────────────────────────────────────────────────

    [Fact]
    public async Task SearchMemory_ValidQuery_CallsSdkWithCorrectParameters()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.RetrieveMemoryRecordsAsync(
                It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrieveMemoryRecordsResponse
            {
                MemoryRecordSummaries = [],
            });

        using var tool = new SemanticMemoryTool("mem-123", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "coffee preference", "namespace": "user:prefs", "top_k": 3}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        mock.Verify(c => c.RetrieveMemoryRecordsAsync(
            It.Is<RetrieveMemoryRecordsRequest>(r =>
                r.MemoryId == "mem-123" &&
                r.Namespace == "user:prefs" &&
                r.SearchCriteria.SearchQuery == "coffee preference" &&
                r.SearchCriteria.TopK == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchMemory_DefaultTopK_Sends5ToSdk()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrieveMemoryRecordsResponse { MemoryRecordSummaries = [] });

        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "namespace": "ns"}""").RootElement;

        await tool.InvokeAsync(input);

        mock.Verify(c => c.RetrieveMemoryRecordsAsync(
            It.Is<RetrieveMemoryRecordsRequest>(r => r.SearchCriteria.TopK == 5),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchMemory_ConfiguredDefaultTopK_UsedWhenNotInToolCall()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrieveMemoryRecordsResponse { MemoryRecordSummaries = [] });

        using var tool = new SemanticMemoryTool("mem-id",
            options: new SemanticMemoryOptions { DefaultTopK = 15 },
            clientOverride: mock.Object);

        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "namespace": "ns"}""").RootElement;
        await tool.InvokeAsync(input);

        mock.Verify(c => c.RetrieveMemoryRecordsAsync(
            It.Is<RetrieveMemoryRecordsRequest>(r => r.SearchCriteria.TopK == 15),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchMemory_ExplicitTopKOverridesDefault()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrieveMemoryRecordsResponse { MemoryRecordSummaries = [] });

        using var tool = new SemanticMemoryTool("mem-id",
            options: new SemanticMemoryOptions { DefaultTopK = 15 },
            clientOverride: mock.Object);

        // Explicit top_k=3 in the tool call should override the configured default of 15
        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "namespace": "ns", "top_k": 3}""").RootElement;
        await tool.InvokeAsync(input);

        mock.Verify(c => c.RetrieveMemoryRecordsAsync(
            It.Is<RetrieveMemoryRecordsRequest>(r => r.SearchCriteria.TopK == 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchMemory_DefaultNamespace_UsedWhenNotInToolCall()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrieveMemoryRecordsResponse { MemoryRecordSummaries = [] });

        using var tool = new SemanticMemoryTool("mem-id",
            options: new SemanticMemoryOptions { DefaultNamespace = "user:alex" },
            clientOverride: mock.Object);

        // No namespace in the tool call — should fall back to DefaultNamespace
        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "coffee"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        mock.Verify(c => c.RetrieveMemoryRecordsAsync(
            It.Is<RetrieveMemoryRecordsRequest>(r => r.Namespace == "user:alex"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchMemory_ExplicitNamespaceOverridesDefault()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrieveMemoryRecordsResponse { MemoryRecordSummaries = [] });

        using var tool = new SemanticMemoryTool("mem-id",
            options: new SemanticMemoryOptions { DefaultNamespace = "user:alex" },
            clientOverride: mock.Object);

        // Explicit namespace in the tool call should override the configured default
        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "coffee", "namespace": "user:bob"}""").RootElement;
        await tool.InvokeAsync(input);

        mock.Verify(c => c.RetrieveMemoryRecordsAsync(
            It.Is<RetrieveMemoryRecordsRequest>(r => r.Namespace == "user:bob"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchMemory_ReturnsRankedResults()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RetrieveMemoryRecordsResponse
            {
                MemoryRecordSummaries =
                [
                    new() { MemoryRecordId = "mem-00000000-0000-0000-0000-000000000001", Content = new MemoryContent { Text = "oat milk" }, Score = 0.9f },
                    new() { MemoryRecordId = "mem-00000000-0000-0000-0000-000000000002", Content = new MemoryContent { Text = "espresso" }, Score = 0.5f },
                ],
            });

        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "coffee", "namespace": "ns"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        var parsed = JsonDocument.Parse(result.Content).RootElement;
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.Equal(2, parsed.GetArrayLength());
        Assert.Equal("mem-00000000-0000-0000-0000-000000000001", parsed[0].GetProperty("memoryRecordId").GetString());
    }

    [Fact]
    public async Task SearchMemory_SdkThrows_ReturnsFailure()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.RetrieveMemoryRecordsAsync(It.IsAny<RetrieveMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonBedrockAgentCoreException("Service error"));

        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "search_memory", "query": "test", "namespace": "ns"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("search_memory failed", result.Content);
    }

    // ── store_memory validation ───────────────────────────────────────────────

    [Fact]
    public async Task StoreMemory_MissingContent_ReturnsFailure()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "store_memory"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("content", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    // ── store_memory SDK call ─────────────────────────────────────────────────

    [Fact]
    public async Task StoreMemory_ValidContent_CallsSdkWithCorrectParameters()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.BatchCreateMemoryRecordsAsync(It.IsAny<BatchCreateMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BatchCreateMemoryRecordsResponse
            {
                SuccessfulRecords = [new MemoryRecordOutput { MemoryRecordId = "mem-00000000-0000-0000-0000-000000000xyz" }],
                FailedRecords = [],
            });

        using var tool = new SemanticMemoryTool("mem-123", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "store_memory", "content": "Alex likes oat milk", "namespace": "user:prefs"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Contains("mem-00000000-0000-0000-0000-000000000xyz", result.Content);
        mock.Verify(c => c.BatchCreateMemoryRecordsAsync(
            It.Is<BatchCreateMemoryRecordsRequest>(r =>
                r.MemoryId == "mem-123" &&
                r.Records.Count == 1 &&
                r.Records[0].Content.Text == "Alex likes oat milk" &&
                r.Records[0].Namespaces.Contains("user:prefs")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreMemory_SdkThrows_ReturnsFailure()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.BatchCreateMemoryRecordsAsync(It.IsAny<BatchCreateMemoryRecordsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonBedrockAgentCoreException("Access denied"));

        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "store_memory", "content": "test"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("store_memory failed", result.Content);
    }

    // ── delete_memory ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteMemory_MissingMemoryRecordId_ReturnsFailure()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        using var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "delete_memory"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("memory_record_id", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteMemory_ValidId_CallsSdkWithCorrectParameters()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        mock.Setup(c => c.DeleteMemoryRecordAsync(It.IsAny<DeleteMemoryRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMemoryRecordResponse());

        using var tool = new SemanticMemoryTool("mem-123", clientOverride: mock.Object);
        var input = JsonDocument.Parse("""{"operation": "delete_memory", "memory_record_id": "mem-abc"}""").RootElement;

        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        mock.Verify(c => c.DeleteMemoryRecordAsync(
            It.Is<DeleteMemoryRecordRequest>(r =>
                r.MemoryId == "mem-123" && r.MemoryRecordId == "mem-abc"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_OwnedClient_DoesNotThrow()
    {
        // When clientOverride is provided, the tool does NOT own the client.
        var mock = new Mock<IAmazonBedrockAgentCore>();
        var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);

        tool.Dispose(); // should not throw
    }

    [Fact]
    public void Dispose_InjectedClient_DoesNotDisposeClient()
    {
        var mock = new Mock<IAmazonBedrockAgentCore>();
        var tool = new SemanticMemoryTool("mem-id", clientOverride: mock.Object);

        tool.Dispose();

        // Injected client should NOT have been disposed
        mock.Verify(c => c.Dispose(), Times.Never);
    }
}
