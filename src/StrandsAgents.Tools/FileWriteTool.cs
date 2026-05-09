using StrandsAgents.Core;
using System.Text;
using System.Text.Json;

namespace StrandsAgents.Tools;

/// <summary>
/// Built-in tool that writes or appends content to a file, scoped to an allowed base path.
/// Path traversal attacks (e.g. <c>../../etc/passwd</c>) are rejected.
/// Parent directories are created automatically if they do not exist.
/// </summary>
public sealed class FileWriteTool : ITool
{
    /// <summary>Default maximum content size: 1 MiB.</summary>
    public const int DefaultMaxContentBytes = 1 * 1024 * 1024;

    private static readonly ToolDefinition _definition = new(
        Name: "file_write",
        Description: "Writes or appends text content to a file within the allowed directory.",
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path":    { "type": "string", "description": "Path to the file, relative to the allowed base directory or absolute within it." },
                "content": { "type": "string", "description": "Text content to write." },
                "append":  { "type": "boolean", "description": "If true, appends to an existing file instead of overwriting. Default: false." }
              },
              "required": ["path", "content"]
            }
            """).RootElement.Clone());

    private readonly string _allowedBasePath;
    private readonly int _maxContentBytes;

    /// <summary>
    /// Initializes a new <see cref="FileWriteTool"/>.
    /// </summary>
    /// <param name="allowedBasePath">
    /// The directory within which file writes are permitted. Any path that resolves
    /// outside this directory is rejected.
    /// </param>
    /// <param name="maxContentBytes">Maximum content size in bytes. Default: 1 MiB.</param>
    public FileWriteTool(string allowedBasePath, int maxContentBytes = DefaultMaxContentBytes)
    {
        ArgumentNullException.ThrowIfNull(allowedBasePath);
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
        _maxContentBytes = maxContentBytes;
    }

    /// <inheritdoc/>
    public ToolDefinition Definition => _definition;

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("path", out var pathEl) ||
            pathEl.GetString() is not { } requestedPath)
            return ToolResult.Failure(Definition.Name, "Missing required field: path.");

        if (!input.TryGetProperty("content", out var contentEl) ||
            contentEl.GetString() is not { } content)
            return ToolResult.Failure(Definition.Name, "Missing required field: content.");

        if (!TryResolveSafePath(requestedPath, out var resolvedPath))
            return ToolResult.Failure(Definition.Name, "Access denied: path is outside the allowed directory.");

        var contentBytes = Encoding.UTF8.GetByteCount(content);
        if (contentBytes > _maxContentBytes)
            return ToolResult.Failure(Definition.Name,
                $"Content exceeds the maximum allowed size of {_maxContentBytes / 1024} KiB.");

        var append = input.TryGetProperty("append", out var appendEl) &&
                     appendEl.ValueKind == JsonValueKind.True;

        try
        {
            var dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            if (append)
                await File.AppendAllTextAsync(resolvedPath, content, ct).ConfigureAwait(false);
            else
                await File.WriteAllTextAsync(resolvedPath, content, ct).ConfigureAwait(false);

            return ToolResult.Success(Definition.Name,
                $"Successfully {(append ? "appended to" : "wrote")} '{Path.GetFileName(resolvedPath)}'.");
        }
        catch (IOException ex)
        {
            return ToolResult.Failure(Definition.Name, $"Failed to write file: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves and validates that <paramref name="requestedPath"/> is within the allowed base.
    /// </summary>
    private bool TryResolveSafePath(string requestedPath, out string resolvedPath)
    {
        try
        {
            resolvedPath = Path.IsPathRooted(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(requestedPath, _allowedBasePath);

            return resolvedPath.StartsWith(
                _allowedBasePath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
                || resolvedPath.Equals(_allowedBasePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            resolvedPath = string.Empty;
            return false;
        }
    }
}
