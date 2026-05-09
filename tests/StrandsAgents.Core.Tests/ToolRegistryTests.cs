using StrandsAgents.Core;
using System.Text.Json;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class ToolRegistryTests
{
    private static ToolCall Call(string id, string name) =>
        new(id, name, JsonDocument.Parse("{}").RootElement);

    [Fact]
    public void Register_And_Resolve_ReturnsCorrectTool()
    {
        var registry = new ToolRegistry();
        var tool = new FakeTool("myTool", ToolResult.Success("id1", "ok"));

        registry.Register(tool);
        var resolved = registry.Resolve("myTool");

        Assert.Same(tool, resolved);
    }

    [Fact]
    public void Resolve_UnknownName_ReturnsNull()
    {
        var registry = new ToolRegistry();

        var resolved = registry.Resolve("nonexistent");

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ExecuteAsync_Sequential_ReturnsAllResults()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool("t1", ToolResult.Success("id1", "result1")));
        registry.Register(new FakeTool("t2", ToolResult.Success("id2", "result2")));
        registry.Register(new FakeTool("t3", ToolResult.Success("id3", "result3")));

        var calls = new[] { Call("id1", "t1"), Call("id2", "t2"), Call("id3", "t3") };
        var results = await registry.ExecuteAsync(calls, parallel: false);

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.ToolCallId == "id1" && r.Content == "result1");
        Assert.Contains(results, r => r.ToolCallId == "id2" && r.Content == "result2");
        Assert.Contains(results, r => r.ToolCallId == "id3" && r.Content == "result3");
    }

    [Fact]
    public async Task ExecuteAsync_Parallel_ResultSetMatchesSequential()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool("p1", ToolResult.Success("pid1", "alpha")));
        registry.Register(new FakeTool("p2", ToolResult.Success("pid2", "beta")));
        registry.Register(new FakeTool("p3", ToolResult.Success("pid3", "gamma")));

        var calls = new[] { Call("pid1", "p1"), Call("pid2", "p2"), Call("pid3", "p3") };

        var seqResults = await registry.ExecuteAsync(calls, parallel: false);
        var parResults = await registry.ExecuteAsync(calls, parallel: true);

        // Order-independent comparison by ToolCallId
        var seqById = seqResults.ToDictionary(r => r.ToolCallId);
        var parById = parResults.ToDictionary(r => r.ToolCallId);

        Assert.Equal(seqById.Keys.OrderBy(k => k), parById.Keys.OrderBy(k => k));
        foreach (var id in seqById.Keys)
        {
            Assert.Equal(seqById[id].Content, parById[id].Content);
            Assert.Equal(seqById[id].IsError, parById[id].IsError);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsErrorResult()
    {
        var registry = new ToolRegistry();

        var calls = new[] { Call("id1", "doesNotExist") };
        var results = await registry.ExecuteAsync(calls, parallel: false);

        Assert.Single(results);
        Assert.True(results[0].IsError);
        Assert.Equal("id1", results[0].ToolCallId);
    }

    [Fact]
    public async Task ExecuteAsync_ToolThrows_ReturnsErrorResult()
    {
        var registry = new ToolRegistry();
        registry.Register(new ThrowingTool("boom"));
        registry.Register(new FakeTool("ok", ToolResult.Success("id2", "fine")));

        var calls = new[] { Call("id1", "boom"), Call("id2", "ok") };
        var results = await registry.ExecuteAsync(calls, parallel: false);

        Assert.Equal(2, results.Count);
        var errorResult = results.First(r => r.ToolCallId == "id1");
        var okResult = results.First(r => r.ToolCallId == "id2");
        Assert.True(errorResult.IsError);
        Assert.False(okResult.IsError);
    }

    [Fact]
    public void GetDefinitions_ReturnsAllRegisteredDefinitions()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeTool("toolA", ToolResult.Success("x", "a")));
        registry.Register(new FakeTool("toolB", ToolResult.Success("x", "b")));
        registry.Register(new FakeTool("toolC", ToolResult.Success("x", "c")));

        var defs = registry.GetDefinitions();

        Assert.Equal(3, defs.Count);
        Assert.Contains(defs, d => d.Name == "toolA");
        Assert.Contains(defs, d => d.Name == "toolB");
        Assert.Contains(defs, d => d.Name == "toolC");
    }
}
