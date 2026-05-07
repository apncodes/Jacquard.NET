using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Strands.Core;
using System.Text.Json;

namespace Strands.Tools.Mcp;

/// <summary>
/// Wraps a discovered MCP tool as an <see cref="ITool"/> so it can be registered
/// in a <see cref="Strands.Core.ToolRegistry"/> and invoked by the agent event loop.
/// </summary>
internal sealed class McpToolWrapper : ITool
{
    private readonly McpClient? _client;
    private readonly McpClientTool? _mcpTool;
    private readonly string _toolName;
    // Injected by the test-only constructor to replace the extension-method call.
    private readonly Func<string, IReadOnlyDictionary<string, object?>, CancellationToken,
        Task<CallToolResult>>? _callOverride;

    internal McpToolWrapper(McpClient client, McpClientTool mcpTool)
    {
        _client = client;
        _mcpTool = mcpTool;
        _toolName = mcpTool.Name;

        // McpClientTool.JsonSchema is a JsonElement describing the input schema.
        // Fall back to an empty object schema if the server did not provide one.
        var schema = mcpTool.JsonSchema.ValueKind != JsonValueKind.Undefined
            ? mcpTool.JsonSchema
            : JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;

        Definition = new ToolDefinition(mcpTool.Name, mcpTool.Description ?? string.Empty, schema);
    }

    // Test-only constructor: avoids constructing McpClientTool and sidesteps the
    // extension-method constraint (Moq cannot mock extension methods).
    internal McpToolWrapper(
        string name,
        string description,
        JsonElement schema,
        Func<string, IReadOnlyDictionary<string, object?>, CancellationToken,
            Task<CallToolResult>> callOverride)
    {
        _client = null;
        _mcpTool = null;
        _toolName = name;
        _callOverride = callOverride;
        Definition = new ToolDefinition(name, description, schema);
    }

    /// <inheritdoc/>
    public ToolDefinition Definition { get; }

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        // Convert the JsonElement input to the IReadOnlyDictionary<string, object?> the MCP SDK expects.
        var arguments = JsonElementToDictionary(input);

        CallToolResult response;
        try
        {
            response = _callOverride is not null
                ? await _callOverride(_toolName, arguments, ct).ConfigureAwait(false)
                : await _client!.CallToolAsync(
                    _toolName,
                    arguments,
                    cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure(Definition.Name, $"MCP tool '{_toolName}' failed: {ex.Message}");
        }

        // Concatenate all text content blocks from the response.
        var text = string.Concat(
            response.Content?
                .OfType<TextContentBlock>()
                .Select(c => c.Text) ?? []);

        return response.IsError == true
            ? ToolResult.Failure(Definition.Name, text)
            : ToolResult.Success(Definition.Name, text);
    }

    /// <summary>
    /// Converts a JsonElement object to a dictionary of native .NET values
    /// suitable for passing to <c>CallToolAsync</c>.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, object?>();

        var dict = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
            dict[prop.Name] = JsonElementToObject(prop.Value);
        return dict;
    }

    private static object? JsonElementToObject(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Object => JsonElementToDictionary(el),
        JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
        _ => null
    };
}
