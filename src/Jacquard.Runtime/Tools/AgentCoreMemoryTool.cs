using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Jacquard.Core;

namespace Jacquard.Runtime.Tools;

/// <summary>
/// Agent-initiated memory operations via Amazon Bedrock AgentCore Memory,
/// backed by the official <c>AWSSDK.BedrockAgentCore</c> SDK client.
///
/// <para>
/// Stores free-text records with optional namespaces, retrieves them by system-assigned
/// <c>memoryRecordId</c>, and deletes them. For semantic (meaning-based) retrieval use
/// <see cref="SemanticMemoryTool"/> instead.
/// </para>
///
/// <para>
/// Authentication is handled automatically by the SDK via the standard AWS credential
/// chain (environment variables, <c>~/.aws/credentials</c>, instance metadata, etc.).
/// </para>
/// </summary>
public sealed class AgentCoreMemoryTool : ITool, IDisposable
{
    private static readonly ToolDefinition _definition = new(
        Name: "agentcore_memory",
        Description: """
            Stores, retrieves, or deletes memory records in Amazon Bedrock AgentCore Memory.

            Records are stored as free-text content with an optional namespace.
            The system assigns a memoryRecordId on creation — use it to retrieve or delete the record.

            Operations:
            - store_memory:    Save a text record. Returns the assigned memoryRecordId.
            - retrieve_memory: Fetch a stored record by its memoryRecordId.
            - delete_memory:   Remove a record by its memoryRecordId.
            """,
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "operation": {
                  "type": "string",
                  "enum": ["store_memory", "retrieve_memory", "delete_memory"],
                  "description": "The memory operation to perform."
                },
                "content": {
                  "type": "string",
                  "description": "The text content to store. Required for store_memory."
                },
                "namespace": {
                  "type": "string",
                  "description": "Optional namespace for the record (e.g. 'user:alex:preferences')."
                },
                "memory_record_id": {
                  "type": "string",
                  "description": "The system-assigned record ID. Required for retrieve_memory and delete_memory."
                }
              },
              "required": ["operation"]
            }
            """).RootElement.Clone());

    private readonly IAmazonBedrockAgentCore _client;
    private readonly string _memoryId;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initialises a new <see cref="AgentCoreMemoryTool"/> using the official AWS SDK client.
    /// </summary>
    /// <param name="memoryId">The AgentCore Memory resource ID.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="IAmazonBedrockAgentCore"/> client. When provided,
    /// the tool does not own the client and will not dispose it. Intended for testing.
    /// </param>
    public AgentCoreMemoryTool(
        string memoryId,
        string region = "us-east-1",
        IAmazonBedrockAgentCore? clientOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        _memoryId = memoryId;
        _ownsClient = clientOverride is null;
        _client = clientOverride ?? new AmazonBedrockAgentCoreClient(
            Amazon.RegionEndpoint.GetBySystemName(region));
    }

    /// <inheritdoc/>
    public ToolDefinition Definition => _definition;

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("operation", out var opEl))
            return ToolResult.Failure("agentcore_memory", "Missing required field: operation.");

        var operation = opEl.GetString();

        return operation switch
        {
            "store_memory"    => await StoreAsync(input, ct).ConfigureAwait(false),
            "retrieve_memory" => await RetrieveAsync(input, ct).ConfigureAwait(false),
            "delete_memory"   => await DeleteAsync(input, ct).ConfigureAwait(false),
            _ => ToolResult.Failure("agentcore_memory",
                $"Unknown operation '{operation}'. Supported: store_memory, retrieve_memory, delete_memory."),
        };
    }

    // ── store_memory ──────────────────────────────────────────────────────────

    private async Task<ToolResult> StoreAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("content", out var contentEl) ||
            contentEl.GetString() is not { Length: > 0 } contentText)
            return ToolResult.Failure("agentcore_memory",
                "content is required for store_memory and must be non-empty.");

        var namespaces = new List<string>();
        if (input.TryGetProperty("namespace", out var nsEl) &&
            nsEl.GetString() is { Length: > 0 } ns)
            namespaces.Add(ns);

        var record = new MemoryRecordCreateInput
        {
            Content = new MemoryContent { Text = contentText },
            Namespaces = namespaces,
            RequestIdentifier = Guid.NewGuid().ToString("N")[..16],
            Timestamp = DateTime.UtcNow,
        };

        var request = new BatchCreateMemoryRecordsRequest
        {
            MemoryId = _memoryId,
            Records = [record],
        };

        try
        {
            var response = await _client.BatchCreateMemoryRecordsAsync(request, ct)
                .ConfigureAwait(false);

            var created = response.SuccessfulRecords?.FirstOrDefault();
            var recordId = created?.MemoryRecordId ?? "unknown";
            return ToolResult.Success("agentcore_memory",
                $"Stored memory record. memoryRecordId: {recordId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure("agentcore_memory", $"store_memory failed: {ex.Message}");
        }
    }

    // ── retrieve_memory ───────────────────────────────────────────────────────

    private async Task<ToolResult> RetrieveAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("memory_record_id", out var idEl) ||
            idEl.GetString() is not { Length: > 0 } recordId)
            return ToolResult.Failure("agentcore_memory",
                "memory_record_id is required for retrieve_memory.");

        var request = new GetMemoryRecordRequest
        {
            MemoryId = _memoryId,
            MemoryRecordId = recordId,
        };

        try
        {
            var response = await _client.GetMemoryRecordAsync(request, ct).ConfigureAwait(false);
            var text = response.MemoryRecord?.Content?.Text ?? string.Empty;
            return ToolResult.Success("agentcore_memory", text);
        }
        catch (Amazon.BedrockAgentCore.Model.ResourceNotFoundException)
        {
            return ToolResult.Success("agentcore_memory",
                $"No memory record found for id: {recordId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure("agentcore_memory", $"retrieve_memory failed: {ex.Message}");
        }
    }

    // ── delete_memory ─────────────────────────────────────────────────────────

    private async Task<ToolResult> DeleteAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("memory_record_id", out var idEl) ||
            idEl.GetString() is not { Length: > 0 } recordId)
            return ToolResult.Failure("agentcore_memory",
                "memory_record_id is required for delete_memory.");

        var request = new DeleteMemoryRecordRequest
        {
            MemoryId = _memoryId,
            MemoryRecordId = recordId,
        };

        try
        {
            await _client.DeleteMemoryRecordAsync(request, ct).ConfigureAwait(false);
            return ToolResult.Success("agentcore_memory", $"Deleted memory record: {recordId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure("agentcore_memory", $"delete_memory failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
