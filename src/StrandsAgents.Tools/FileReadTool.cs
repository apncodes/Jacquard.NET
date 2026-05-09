using StrandsAgents.Core;
using System.Text.Json;

namespace StrandsAgents.Tools;

/// <summary>
/// Built-in tool that reads a file from the filesystem, scoped to an allowed base path.
/// Path traversal attacks (e.g. <c>../../etc/passwd</c>) are rejected.
/// </summary>
public sealed class FileReadTool : ITool
{
    /// <summary>Default maximum file size: 1 MiB.</summary>
    public const int DefaultMaxSizeBytes = 1 * 1024 * 1024;

    private static readonly ToolDefinition _definition = new(
        Name: "file_read",
        Description: "Reads a file from the allowed directory and returns its contents as a string.",
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Path to the file, relative to the allowed base directory or absolute within it." }
              },
              "required": ["path"]
            }
            """).RootElement.Clone());

    private readonly string _allowedBasePath;
    private readonly int _maxSizeBytes;

    /// <summary>
    /// Initializes a new <see cref="FileReadTool"/>.
    /// </summary>
    /// <param name="allowedBasePath">
    /// The directory within which file reads are permitted. Any path that resolves
    /// outside this directory is rejected.
    /// </param>
    /// <param name="maxSizeBytes">Maximum file size in bytes. Default: 1 MiB.</param>
    public FileReadTool(string allowedBasePath, int maxSizeBytes = DefaultMaxSizeBytes)
    {
        ArgumentNullException.ThrowIfNull(allowedBasePath);
        _allowedBasePath = Path.GetFullPath(allowedBasePath);
        _maxSizeBytes = maxSizeBytes;
    }

    /// <inheritdoc/>
    public ToolDefinition Definition => _definition;

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("path", out var pathEl) ||
            pathEl.GetString() is not { } requestedPath)
            return ToolResult.Failure(Definition.Name, "Missing required field: path.");

        if (!TryResolveSafePath(requestedPath, out var resolvedPath))
            return ToolResult.Failure(Definition.Name, "Access denied: path is outside the allowed directory.");

        if (!File.Exists(resolvedPath))
            return ToolResult.Failure(Definition.Name, "File not found.");

        var info = new FileInfo(resolvedPath);
        if (info.Length > _maxSizeBytes)
            return ToolResult.Failure(Definition.Name,
                $"File exceeds the maximum allowed size of {_maxSizeBytes / 1024} KiB.");

        try
        {
            var content = await File.ReadAllTextAsync(resolvedPath, ct).ConfigureAwait(false);
            return ToolResult.Success(Definition.Name, content);
        }
        catch (IOException ex)
        {
            return ToolResult.Failure(Definition.Name, $"Failed to read file: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves and validates that <paramref name="requestedPath"/> is within the allowed base.
    /// Uses <see cref="Path.GetFullPath"/> to canonicalize and detect traversal.
    /// </summary>
    private bool TryResolveSafePath(string requestedPath, out string resolvedPath)
    {
        try
        {
            // Resolve relative to the allowed base or as absolute.
            resolvedPath = Path.IsPathRooted(requestedPath)
                ? Path.GetFullPath(requestedPath)
                : Path.GetFullPath(requestedPath, _allowedBasePath);

            // The resolved path must start with the allowed base (with trailing separator
            // to prevent "/allowed/base-other" from matching "/allowed/base").
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
