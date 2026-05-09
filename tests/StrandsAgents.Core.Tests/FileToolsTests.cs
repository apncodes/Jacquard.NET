using StrandsAgents.Tools;
using System.Text.Json;
using Xunit;

namespace StrandsAgents.Core.Tests;

public class FileToolsTests : IDisposable
{
    private readonly string _tempDir;

    public FileToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── FileReadTool ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FileReadTool_ValidFile_ReturnsContent()
    {
        var filePath = Path.Combine(_tempDir, "hello.txt");
        await File.WriteAllTextAsync(filePath, "hello world");

        var tool = new FileReadTool(_tempDir);
        var input = JsonDocument.Parse($$"""{"path":"{{filePath.Replace("\\", "\\\\")}}" }""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Equal("hello world", result.Content);
    }

    [Fact]
    public async Task FileReadTool_PathTraversal_ReturnsDenied()
    {
        var tool = new FileReadTool(_tempDir);
        var traversalPath = Path.Combine(_tempDir, "..", "etc", "passwd");
        var input = JsonDocument.Parse($$"""{"path":"{{traversalPath.Replace("\\", "\\\\")}}" }""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("Access denied", result.Content);
    }

    [Fact]
    public async Task FileReadTool_FileTooLarge_ReturnsFailure()
    {
        var filePath = Path.Combine(_tempDir, "big.txt");
        // Write 10 bytes, cap at 5 bytes
        await File.WriteAllTextAsync(filePath, "0123456789");

        var tool = new FileReadTool(_tempDir, maxSizeBytes: 5);
        var input = JsonDocument.Parse($$"""{"path":"{{filePath.Replace("\\", "\\\\")}}" }""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("maximum allowed size", result.Content);
    }

    [Fact]
    public async Task FileReadTool_MissingFile_ReturnsFailure()
    {
        var tool = new FileReadTool(_tempDir);
        var missing = Path.Combine(_tempDir, "nonexistent.txt");
        var input = JsonDocument.Parse($$"""{"path":"{{missing.Replace("\\", "\\\\")}}" }""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Content);
    }

    [Fact]
    public async Task FileReadTool_RelativePath_ResolvesWithinBase()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "rel.txt"), "relative content");

        var tool = new FileReadTool(_tempDir);
        var input = JsonDocument.Parse("""{"path":"rel.txt"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Equal("relative content", result.Content);
    }

    // ── FileWriteTool ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FileWriteTool_Write_CreatesFile()
    {
        var tool = new FileWriteTool(_tempDir);
        var targetPath = Path.Combine(_tempDir, "out.txt");
        var input = JsonDocument.Parse($$"""{"path":"{{targetPath.Replace("\\", "\\\\")}}", "content":"written"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Equal("written", await File.ReadAllTextAsync(targetPath));
    }

    [Fact]
    public async Task FileWriteTool_Append_AppendsToExistingFile()
    {
        var targetPath = Path.Combine(_tempDir, "append.txt");
        await File.WriteAllTextAsync(targetPath, "first");

        var tool = new FileWriteTool(_tempDir);
        var input = JsonDocument.Parse($$"""{"path":"{{targetPath.Replace("\\", "\\\\")}}", "content":" second", "append":true}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Equal("first second", await File.ReadAllTextAsync(targetPath));
    }

    [Fact]
    public async Task FileWriteTool_PathTraversal_ReturnsDenied()
    {
        var tool = new FileWriteTool(_tempDir);
        var traversalPath = Path.Combine(_tempDir, "..", "evil.txt");
        var input = JsonDocument.Parse($$"""{"path":"{{traversalPath.Replace("\\", "\\\\")}}", "content":"hack"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("Access denied", result.Content);
    }

    [Fact]
    public async Task FileWriteTool_ContentTooLarge_ReturnsFailure()
    {
        var tool = new FileWriteTool(_tempDir, maxContentBytes: 5);
        var targetPath = Path.Combine(_tempDir, "large.txt");
        var input = JsonDocument.Parse($$"""{"path":"{{targetPath.Replace("\\", "\\\\")}}", "content":"0123456789"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.True(result.IsError);
        Assert.Contains("maximum allowed size", result.Content);
    }

    [Fact]
    public async Task FileWriteTool_CreatesParentDirectories()
    {
        var tool = new FileWriteTool(_tempDir);
        var nestedPath = Path.Combine(_tempDir, "subdir", "nested.txt");
        var input = JsonDocument.Parse($$"""{"path":"{{nestedPath.Replace("\\", "\\\\")}}", "content":"deep"}""").RootElement;
        var result = await tool.InvokeAsync(input);

        Assert.False(result.IsError);
        Assert.Equal("deep", await File.ReadAllTextAsync(nestedPath));
    }
}
