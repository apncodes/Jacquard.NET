using System.Text.Json;
using StrandsAgents.Core;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class InMemorySessionManagerTests
{
    private static AgentSession MakeSession(string id) => new AgentSession(
        id,
        [Message.User("hello"), Message.Assistant("world")],
        new Dictionary<string, object?> { ["key"] = "value" },
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task SaveThenLoad_SameId_ReturnsSameSession()
    {
        var manager = new InMemorySessionManager();
        var session = MakeSession("s1");

        await manager.SaveAsync("s1", session);
        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
        Assert.Equal("s1", loaded.SessionId);
        Assert.Equal(2, loaded.Messages.Count);
    }

    [Fact]
    public async Task Load_UnknownId_ReturnsNull()
    {
        var manager = new InMemorySessionManager();

        var result = await manager.LoadAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task MultipleSessionsLoadIndependently()
    {
        var manager = new InMemorySessionManager();
        var s1 = MakeSession("s1");
        var s2 = new AgentSession("s2", [Message.User("other")], new Dictionary<string, object?>(), DateTimeOffset.UtcNow);

        await manager.SaveAsync("s1", s1);
        await manager.SaveAsync("s2", s2);

        var loaded1 = await manager.LoadAsync("s1");
        var loaded2 = await manager.LoadAsync("s2");

        Assert.Equal("s1", loaded1!.SessionId);
        Assert.Equal(2, loaded1.Messages.Count);
        Assert.Equal("s2", loaded2!.SessionId);
        Assert.Single(loaded2.Messages);
    }

    [Fact]
    public async Task SaveTwiceSameId_SecondOverwritesFirst()
    {
        var manager = new InMemorySessionManager();
        var first = MakeSession("s1");
        var second = new AgentSession("s1", [Message.User("updated")], new Dictionary<string, object?>(), DateTimeOffset.UtcNow);

        await manager.SaveAsync("s1", first);
        await manager.SaveAsync("s1", second);
        var loaded = await manager.LoadAsync("s1");

        Assert.NotNull(loaded);
        Assert.Single(loaded.Messages);
        Assert.Equal("updated", ((TextBlock)loaded.Messages[0].Content[0]).Text);
    }
}

public class FileSessionManagerTests : IDisposable
{
    private readonly string _dir;

    public FileSessionManagerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static AgentSession MakeSession(string id) => new AgentSession(
        id,
        [Message.User("hello"), Message.Assistant("world")],
        new Dictionary<string, object?> { ["key"] = "value" },
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task SaveAsync_CreatesFileAtExpectedPath()
    {
        var manager = new FileSessionManager(_dir);
        var session = MakeSession("abc");

        await manager.SaveAsync("abc", session);

        Assert.True(File.Exists(Path.Combine(_dir, "abc.json")));
    }

    [Fact]
    public async Task LoadAfterSave_ReturnsSameMessagesAndState()
    {
        var manager = new FileSessionManager(_dir);
        var session = MakeSession("round-trip");

        await manager.SaveAsync("round-trip", session);
        var loaded = await manager.LoadAsync("round-trip");

        Assert.NotNull(loaded);
        Assert.Equal("round-trip", loaded.SessionId);
        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("hello", ((TextBlock)loaded.Messages[0].Content[0]).Text);
        Assert.Equal("world", ((TextBlock)loaded.Messages[1].Content[0]).Text);
        Assert.Equal("value", loaded.State["key"]?.ToString());
    }

    [Fact]
    public async Task Load_UnknownId_ReturnsNull()
    {
        var manager = new FileSessionManager(_dir);

        var result = await manager.LoadAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task RoundTrip_PreservesAllContentBlockTypes()
    {
        var manager = new FileSessionManager(_dir);
        var toolInput = JsonDocument.Parse("""{"x":1}""").RootElement.Clone();
        var session = new AgentSession(
            "blocks",
            [
                new Message(Role.User, [new TextBlock("hi")]),
                new Message(Role.Assistant, [new ToolUseBlock("id1", "my_tool", toolInput)]),
                new Message(Role.User, [new ToolResultBlock("id1", "result", false)])
            ],
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

        await manager.SaveAsync("blocks", session);
        var loaded = await manager.LoadAsync("blocks");

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.Messages.Count);

        var text = Assert.IsType<TextBlock>(loaded.Messages[0].Content[0]);
        Assert.Equal("hi", text.Text);

        var toolUse = Assert.IsType<ToolUseBlock>(loaded.Messages[1].Content[0]);
        Assert.Equal("id1", toolUse.Id);
        Assert.Equal("my_tool", toolUse.Name);
        Assert.Equal(1, toolUse.Input.GetProperty("x").GetInt32());

        var toolResult = Assert.IsType<ToolResultBlock>(loaded.Messages[2].Content[0]);
        Assert.Equal("id1", toolResult.ToolUseId);
        Assert.Equal("result", toolResult.Content);
        Assert.False(toolResult.IsError);
    }

    [Fact]
    public async Task RoundTrip_PreservesStateDictionary()
    {
        var manager = new FileSessionManager(_dir);
        var state = new Dictionary<string, object?> { ["count"] = 42, ["name"] = "test", ["flag"] = true };
        var session = new AgentSession("state-test", [], state, DateTimeOffset.UtcNow);

        await manager.SaveAsync("state-test", session);
        var loaded = await manager.LoadAsync("state-test");

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded.State.Count);
        // Values come back as JsonElement after deserialization
        Assert.Equal("test", loaded.State["name"]?.ToString());
    }

    [Fact]
    public void Constructor_CreatesDirectoryIfNotExists()
    {
        var newDir = Path.Combine(_dir, "subdir", "nested");

        _ = new FileSessionManager(newDir);

        Assert.True(Directory.Exists(newDir));
    }
}
