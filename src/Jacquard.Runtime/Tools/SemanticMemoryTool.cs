using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Jacquard.Core;

namespace Jacquard.Runtime.Tools;

/// <summary>
/// Configuration options for <see cref="SemanticMemoryTool"/>.
/// </summary>
public sealed class SemanticMemoryOptions
{
    private int _defaultTopK = 5;

    /// <summary>
    /// Default number of results returned by <c>search_memory</c> when the LLM does not
    /// specify <c>top_k</c> in the tool call. Must be in the range [1, 100]. Default: 5.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the value is less than 1 or greater than 100.
    /// </exception>
    public int DefaultTopK
    {
        get => _defaultTopK;
        init
        {
            if (value is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(DefaultTopK), value,
                    "DefaultTopK must be between 1 and 100.");
            _defaultTopK = value;
        }
    }

    /// <summary>
    /// Default namespace used for <c>store_memory</c> and <c>search_memory</c> when the
    /// LLM does not specify a <c>namespace</c> in the tool call. When <c>null</c> (the
    /// default), namespace is required in every tool call.
    /// </summary>
    public string? DefaultNamespace { get; init; }
}

/// <summary>
/// Agent-initiated semantic (vector) memory operations via Amazon Bedrock AgentCore Memory,
/// backed by the official <c>AWSSDK.BedrockAgentCore</c> SDK client.
///
/// <para>
/// Unlike <see cref="AgentCoreMemoryTool"/> which retrieves by exact record ID, this tool
/// exposes a <c>search_memory</c> operation that retrieves records by semantic similarity —
/// the agent describes what it is looking for in natural language and receives the closest
/// matches ranked by relevance score.
/// </para>
///
/// <para>
/// Behaviour can be tuned via <see cref="SemanticMemoryOptions"/>:
/// <list type="bullet">
///   <item><see cref="SemanticMemoryOptions.DefaultTopK"/> — how many results to return when
///   the LLM does not specify <c>top_k</c>.</item>
///   <item><see cref="SemanticMemoryOptions.DefaultNamespace"/> — namespace to use when the
///   LLM does not specify one, useful when all memories live in a single namespace.</item>
/// </list>
/// </para>
///
/// <para>
/// Authentication is handled automatically by the SDK via the standard AWS credential
/// chain (environment variables, <c>~/.aws/credentials</c>, instance metadata, etc.).
/// </para>
/// </summary>
public sealed class SemanticMemoryTool : ITool, IDisposable
{
    private const string ToolName = "agentcore_semantic_memory";

    // API constraints from AgentCore — not configurable.
    private const int MinTopK = 1;
    private const int MaxTopK = 100;

    private readonly IAmazonBedrockAgentCore _client;
    private readonly string _memoryId;
    private readonly SemanticMemoryOptions _options;
    private readonly bool _ownsClient;

    // ToolDefinition is instance-level (not static) so the description can reflect
    // the configured DefaultTopK and DefaultNamespace.
    private readonly ToolDefinition _definition;

    /// <summary>
    /// Initialises a new <see cref="SemanticMemoryTool"/> using the official AWS SDK client.
    /// </summary>
    /// <param name="memoryId">The AgentCore Memory resource ID.</param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="options">
    /// Optional configuration. When <c>null</c>, defaults are used
    /// (<see cref="SemanticMemoryOptions.DefaultTopK"/> = 5, no default namespace).
    /// </param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="IAmazonBedrockAgentCore"/> client. When provided,
    /// the tool does not own the client and will not dispose it. Intended for testing.
    /// </param>
    public SemanticMemoryTool(
        string memoryId,
        string region = "us-east-1",
        SemanticMemoryOptions? options = null,
        IAmazonBedrockAgentCore? clientOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        _memoryId = memoryId;
        _options = options ?? new SemanticMemoryOptions();
        _ownsClient = clientOverride is null;
        _client = clientOverride ?? new AmazonBedrockAgentCoreClient(
            Amazon.RegionEndpoint.GetBySystemName(region));

        _definition = BuildDefinition(_options);
    }

    /// <inheritdoc/>
    public ToolDefinition Definition => _definition;

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("operation", out var opEl))
            return ToolResult.Failure(ToolName, "Missing required field: operation.");

        var operation = opEl.GetString();

        return operation switch
        {
            "store_memory"  => await HandleStoreAsync(input, ct).ConfigureAwait(false),
            "search_memory" => await HandleSearchAsync(input, ct).ConfigureAwait(false),
            "delete_memory" => await HandleDeleteAsync(input, ct).ConfigureAwait(false),
            _ => ToolResult.Failure(ToolName,
                $"Unknown operation '{operation}'. Supported: store_memory, search_memory, delete_memory."),
        };
    }

    // ── store_memory ──────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleStoreAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("content", out var contentEl) ||
            contentEl.GetString() is not { Length: > 0 } contentText)
            return ToolResult.Failure(ToolName, "content is required for store_memory and must be non-empty.");

        // Resolve namespace: tool call → DefaultNamespace → empty list
        var ns = ResolveNamespace(input);
        var namespaces = ns is not null ? new List<string> { ns } : new List<string>();

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
            return ToolResult.Success(ToolName, $"Stored memory record. memoryRecordId: {recordId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure(ToolName, $"store_memory failed: {ex.Message}");
        }
    }

    // ── search_memory ─────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleSearchAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("query", out var queryEl) ||
            queryEl.GetString() is not { Length: > 0 } query)
            return ToolResult.Failure(ToolName, "query must be a non-empty string.");

        // Resolve namespace: tool call → DefaultNamespace → error
        var ns = ResolveNamespace(input);
        if (ns is null)
            return ToolResult.Failure(ToolName,
                "namespace is required for search_memory. " +
                "Either pass it in the tool call or set SemanticMemoryOptions.DefaultNamespace.");

        // Resolve top_k: tool call → DefaultTopK
        var topK = _options.DefaultTopK;
        if (input.TryGetProperty("top_k", out var topKEl) && topKEl.ValueKind == JsonValueKind.Number)
        {
            topK = topKEl.GetInt32();
            if (topK < MinTopK || topK > MaxTopK)
                return ToolResult.Failure(ToolName,
                    $"top_k must be between {MinTopK} and {MaxTopK}. Got: {topK}.");
        }

        var request = new RetrieveMemoryRecordsRequest
        {
            MemoryId = _memoryId,
            Namespace = ns,
            SearchCriteria = new SearchCriteria
            {
                SearchQuery = query,
                TopK = topK,
            },
        };

        try
        {
            var response = await _client.RetrieveMemoryRecordsAsync(request, ct)
                .ConfigureAwait(false);

            var summaries = response.MemoryRecordSummaries ?? [];
            var output = summaries.Select(s => new
            {
                memoryRecordId = s.MemoryRecordId,
                content = s.Content?.Text ?? string.Empty,
                score = s.Score,
                namespaces = s.Namespaces ?? [],
            }).ToList();

            return ToolResult.Success(ToolName,
                JsonSerializer.Serialize(output, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure(ToolName, $"search_memory failed: {ex.Message}");
        }
    }

    // ── delete_memory ─────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleDeleteAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("memory_record_id", out var idEl) ||
            idEl.GetString() is not { Length: > 0 } recordId)
            return ToolResult.Failure(ToolName, "memory_record_id is required for delete_memory.");

        var request = new DeleteMemoryRecordRequest
        {
            MemoryId = _memoryId,
            MemoryRecordId = recordId,
        };

        try
        {
            await _client.DeleteMemoryRecordAsync(request, ct).ConfigureAwait(false);
            return ToolResult.Success(ToolName, $"Deleted memory record: {recordId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure(ToolName, $"delete_memory failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the namespace from the tool call input, falling back to
    /// <see cref="SemanticMemoryOptions.DefaultNamespace"/> when not provided.
    /// Returns <c>null</c> when neither is available.
    /// </summary>
    private string? ResolveNamespace(JsonElement input)
    {
        if (input.TryGetProperty("namespace", out var nsEl) &&
            nsEl.GetString() is { Length: > 0 } ns)
            return ns;

        return _options.DefaultNamespace;
    }

    /// <summary>
    /// Builds the <see cref="ToolDefinition"/> with a description that reflects the
    /// configured defaults, so the LLM sees accurate documentation.
    /// </summary>
    private static ToolDefinition BuildDefinition(SemanticMemoryOptions opts)
    {
        var nsNote = opts.DefaultNamespace is not null
            ? $" Defaults to '{opts.DefaultNamespace}' when omitted."
            : " Required for store_memory and search_memory when no default is configured.";

        var topKNote = $"Maximum number of results for search_memory. Range: {MinTopK}–{MaxTopK}. Default: {opts.DefaultTopK}.";

        var schema = $$"""
            {
              "type": "object",
              "properties": {
                "operation": {
                  "type": "string",
                  "enum": ["store_memory", "search_memory", "delete_memory"],
                  "description": "The memory operation to perform."
                },
                "content": {
                  "type": "string",
                  "description": "The text content to store. Required for store_memory."
                },
                "namespace": {
                  "type": "string",
                  "description": "Namespace for the record (e.g. 'user:alex:preferences').{{nsNote}}"
                },
                "query": {
                  "type": "string",
                  "description": "Natural-language search query. Required for search_memory."
                },
                "top_k": {
                  "type": "integer",
                  "description": "{{topKNote}}"
                },
                "memory_record_id": {
                  "type": "string",
                  "description": "The system-assigned record ID. Required for delete_memory."
                }
              },
              "required": ["operation"]
            }
            """;

        var defaultNsLine = opts.DefaultNamespace is not null
            ? $"Default namespace: {opts.DefaultNamespace}."
            : "Namespace must be provided per call.";

        return new ToolDefinition(
            Name: ToolName,
            Description:
                $"""
                Stores, searches, or deletes memory records in Amazon Bedrock AgentCore Memory.

                Records are stored as free-text content with a namespace (e.g. "user:alex:preferences").
                Use search_memory to retrieve records by meaning rather than exact ID.
                The system assigns a memoryRecordId on creation — use it to delete a specific record.

                Default top_k: {opts.DefaultTopK}. Range: {MinTopK}–{MaxTopK}.
                {defaultNsLine}

                Operations:
                - store_memory:  Save a text record with a namespace. Returns the assigned memoryRecordId.
                - search_memory: Find records semantically similar to a query string within a namespace.
                                 Returns a ranked list of memoryRecordId, content, score.
                - delete_memory: Remove a record by its memoryRecordId.
                """,
            InputSchema: JsonDocument.Parse(schema).RootElement.Clone());
    }
}
