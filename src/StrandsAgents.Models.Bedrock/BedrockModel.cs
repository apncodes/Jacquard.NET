using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Amazon.Runtime.Documents;
using Amazon.Runtime.EventStreams.Internal;
using Polly;
using Polly.Retry;
using System.Text.Json;
using System.Threading.Channels;

namespace StrandsAgents.Models.Bedrock;

/// <summary>
/// Amazon Bedrock model provider using the Converse API.
/// Default model: us.anthropic.claude-sonnet-4-20250514-v1:0 (cross-region inference profile).
/// Use a cross-region profile ID (e.g. us.anthropic.claude-*) — bare model IDs require
/// on-demand throughput which is not available by default.
/// Retries up to 3 times with exponential backoff and jitter on throttling errors.
/// </summary>
public sealed class BedrockModel : StrandsAgents.Core.IModel
{
    private readonly IAmazonBedrockRuntime _client;
    private readonly string _modelId;
    private readonly ResiliencePipeline _retryPipeline;

    /// <summary>
    /// Initializes a new <see cref="BedrockModel"/> with the given region and model.
    /// </summary>
    /// <param name="region">AWS region name (e.g. "us-east-1").</param>
    /// <param name="modelId">Bedrock cross-region inference profile ID.</param>
    /// <param name="config">Optional custom <see cref="AmazonBedrockRuntimeConfig"/>. When provided, <paramref name="region"/> is ignored.</param>
    /// <param name="clientOverride">
    /// An existing <see cref="IAmazonBedrockRuntime"/> client to use instead of creating one.
    /// Intended for unit testing — pass a mock to avoid live AWS calls.
    /// </param>
    public BedrockModel(
        string region = "us-east-1",
        string modelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0",
        AmazonBedrockRuntimeConfig? config = null,
        IAmazonBedrockRuntime? clientOverride = null)
    {
        _modelId = modelId;

        if (clientOverride is not null)
        {
            _client = clientOverride;
        }
        else
        {
            var cfg = config ?? new AmazonBedrockRuntimeConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
            };
            _client = new AmazonBedrockRuntimeClient(cfg);
        }

        _retryPipeline = BuildRetryPipeline();
    }

    /// <inheritdoc/>
    public async Task<StrandsAgents.Core.ModelResponse> InvokeAsync(
        StrandsAgents.Core.ModelRequest request,
        CancellationToken ct = default)
    {
        var converseRequest = BuildConverseRequest(request);

        var response = await _retryPipeline.ExecuteAsync(
            async token => await _client.ConverseAsync(converseRequest, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        return MapResponse(response);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StrandsAgents.Core.ModelStreamEvent> StreamAsync(
        StrandsAgents.Core.ModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var converseRequest = BuildConverseRequest(request);
        var streamRequest = new ConverseStreamRequest
        {
            ModelId = converseRequest.ModelId,
            Messages = converseRequest.Messages,
            System = converseRequest.System,
            ToolConfig = converseRequest.ToolConfig,
            InferenceConfig = converseRequest.InferenceConfig
        };

        // Retry only covers the initial stream establishment — not mid-stream events.
        var response = await _retryPipeline.ExecuteAsync(
            async token => await _client.ConverseStreamAsync(streamRequest, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        string? currentToolId = null;
        string? currentToolName = null;
        string? textContent = null;
        var toolInputBuffers = new Dictionary<string, System.Text.StringBuilder>();
        var toolCalls = new List<StrandsAgents.Core.ToolCall>();
        StrandsAgents.Core.TokenUsage usage = StrandsAgents.Core.TokenUsage.Zero;
        StrandsAgents.Core.StopReason stopReason = StrandsAgents.Core.StopReason.EndTurn;

        var stream = response.Stream;

        // The AWS SDK event stream exposes a synchronous IEnumerable. Bridge it to
        // IAsyncEnumerable via a channel so callers receive tokens as they arrive
        // rather than waiting for the entire response to buffer.
        var channel = Channel.CreateUnbounded<IEventStreamEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        // Use enumerable traversal only — StartProcessing() (event-driven) and
        // GetEnumerator() (enumerable) are mutually exclusive on the AWS SDK stream.
        var fillTask = Task.Run(() =>
        {
            try
            {
                foreach (var e in stream)
                    channel.Writer.TryWrite(e);
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        await foreach (var evt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case ContentBlockDeltaEvent delta when delta.Delta?.Text is not null:
                    textContent = (textContent ?? "") + delta.Delta.Text;
                    yield return new StrandsAgents.Core.TextDeltaModelEvent(delta.Delta.Text);
                    break;

                case ContentBlockStartEvent start when start.Start?.ToolUse is not null:
                    currentToolId = start.Start.ToolUse.ToolUseId;
                    currentToolName = start.Start.ToolUse.Name;
                    toolInputBuffers[currentToolId!] = new System.Text.StringBuilder();
                    yield return new StrandsAgents.Core.ToolCallStartModelEvent(currentToolId!, currentToolName!);
                    break;

                case ContentBlockDeltaEvent toolDelta when toolDelta.Delta?.ToolUse?.Input is not null:
                    if (currentToolId is not null && toolInputBuffers.TryGetValue(currentToolId, out var sb))
                    {
                        sb.Append(toolDelta.Delta.ToolUse.Input);
                        yield return new StrandsAgents.Core.ToolCallInputDeltaModelEvent(currentToolId, toolDelta.Delta.ToolUse.Input);
                    }
                    break;

                case ContentBlockStopEvent when currentToolId is not null && currentToolName is not null:
                    if (toolInputBuffers.TryGetValue(currentToolId, out var inputSb))
                    {
                        var inputJson = inputSb.Length > 0 ? inputSb.ToString() : "{}";
                        toolCalls.Add(new StrandsAgents.Core.ToolCall(
                            currentToolId,
                            currentToolName,
                            JsonDocument.Parse(inputJson).RootElement));
                    }
                    currentToolId = null;
                    currentToolName = null;
                    break;

                case MessageStopEvent stop:
                    stopReason = MapStopReason(stop.StopReason);
                    break;

                case ConverseStreamMetadataEvent meta:
                    usage = new StrandsAgents.Core.TokenUsage(
                        meta.Usage?.InputTokens ?? 0,
                        meta.Usage?.OutputTokens ?? 0);
                    break;
            }
        }

        // Propagate any exception thrown by the background stream reader.
        await fillTask.ConfigureAwait(false);

        var finalResponse = new StrandsAgents.Core.ModelResponse(textContent, toolCalls, stopReason, usage);
        yield return new StrandsAgents.Core.ModelCompleteEvent(finalResponse);
    }

    /// <summary>
    /// Builds the Polly retry pipeline: up to 3 retries on ThrottlingException,
    /// with exponential backoff (2^attempt seconds) plus random jitter (0–500 ms).
    /// CancellationToken cancels retry waits.
    /// </summary>
    private static ResiliencePipeline BuildRetryPipeline() =>
        new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                ShouldHandle = new PredicateBuilder()
                    .Handle<AmazonServiceException>(ex =>
                        ex.ErrorCode == "ThrottlingException" ||
                        ex.ErrorCode == "ServiceUnavailableException" ||
                        ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests),
                DelayGenerator = static args =>
                {
                    var baseDelay = TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber));
                    var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                    return ValueTask.FromResult<TimeSpan?>(baseDelay + jitter);
                }
            })
            .Build();

    private ConverseRequest BuildConverseRequest(StrandsAgents.Core.ModelRequest request)
    {
        var messages = request.Messages
            .Select(MapMessage)
            .ToList();

        var system = request.SystemPrompt is not null
            ? new List<SystemContentBlock> { new SystemContentBlock { Text = request.SystemPrompt } }
            : null;

        var toolConfig = request.Tools.Count > 0
            ? new ToolConfiguration { Tools = request.Tools.Select(MapTool).ToList() }
            : null;

        var inferenceConfig = new InferenceConfiguration();
        if (request.Parameters.MaxTokens.HasValue)
            inferenceConfig.MaxTokens = request.Parameters.MaxTokens.Value;
        if (request.Parameters.Temperature.HasValue)
            inferenceConfig.Temperature = request.Parameters.Temperature.Value;

        return new ConverseRequest
        {
            ModelId = request.Parameters.ModelId ?? _modelId,
            Messages = messages,
            System = system,
            ToolConfig = toolConfig,
            InferenceConfig = inferenceConfig
        };
    }

    private static Amazon.BedrockRuntime.Model.Message MapMessage(StrandsAgents.Core.Message msg)
    {
        var blocks = msg.Content.Select(MapContentBlock).ToList();
        return new Amazon.BedrockRuntime.Model.Message
        {
            Role = msg.Role == StrandsAgents.Core.Role.User ? ConversationRole.User : ConversationRole.Assistant,
            Content = blocks
        };
    }

    private static ContentBlock MapContentBlock(StrandsAgents.Core.ContentBlock block) => block switch
    {
        StrandsAgents.Core.TextBlock t => new ContentBlock { Text = t.Text },
        StrandsAgents.Core.ToolUseBlock tu => new ContentBlock
        {
            ToolUse = new ToolUseBlock
            {
                ToolUseId = tu.Id,
                Name = tu.Name,
                Input = JsonElementToDocument(tu.Input)
            }
        },
        StrandsAgents.Core.ToolResultBlock tr => new ContentBlock
        {
            ToolResult = new ToolResultBlock
            {
                ToolUseId = tr.ToolUseId,
                Content = new List<ToolResultContentBlock>
                {
                    new ToolResultContentBlock { Text = tr.Content }
                },
                Status = tr.IsError ? ToolResultStatus.Error : ToolResultStatus.Success
            }
        },
        _ => new ContentBlock { Text = string.Empty }
    };

    private static Tool MapTool(StrandsAgents.Core.ToolDefinition def)
    {
        return new Tool
        {
            ToolSpec = new ToolSpecification
            {
                Name = def.Name,
                Description = def.Description,
                InputSchema = new ToolInputSchema
                {
                    Json = JsonElementToDocument(def.InputSchema)
                }
            }
        };
    }

    private static StrandsAgents.Core.ModelResponse MapResponse(ConverseResponse response)
    {
        string? text = null;
        var toolCalls = new List<StrandsAgents.Core.ToolCall>();

        foreach (var block in response.Output?.Message?.Content ?? new List<ContentBlock>())
        {
            if (block.Text is not null)
                text = block.Text;
            if (block.ToolUse is not null)
            {
                var inputJson = DocumentToJson(block.ToolUse.Input);
                toolCalls.Add(new StrandsAgents.Core.ToolCall(
                    block.ToolUse.ToolUseId,
                    block.ToolUse.Name,
                    JsonDocument.Parse(inputJson).RootElement));
            }
        }

        var usage = new StrandsAgents.Core.TokenUsage(
            response.Usage?.InputTokens ?? 0,
            response.Usage?.OutputTokens ?? 0);

        return new StrandsAgents.Core.ModelResponse(text, toolCalls, MapStopReason(response.StopReason), usage);
    }

    private static StrandsAgents.Core.StopReason MapStopReason(Amazon.BedrockRuntime.StopReason? reason)
    {
        if (reason == Amazon.BedrockRuntime.StopReason.Tool_use)
            return StrandsAgents.Core.StopReason.ToolUse;
        if (reason == Amazon.BedrockRuntime.StopReason.Max_tokens)
            return StrandsAgents.Core.StopReason.MaxTokens;
        if (reason == Amazon.BedrockRuntime.StopReason.Stop_sequence)
            return StrandsAgents.Core.StopReason.StopSequence;
        return StrandsAgents.Core.StopReason.EndTurn;
    }

    /// <summary>Converts a JsonElement to an AWS Document for use in Bedrock API calls.</summary>
    private static Document JsonElementToDocument(JsonElement element) =>
        Document.FromObject(JsonElementToNative(element));

    /// <summary>
    /// Recursively converts a JsonElement to a native .NET object tree
    /// (Dictionary / List / primitives) that the AWS SDK's LitJson serializer understands.
    /// JsonSerializer.Deserialize&lt;object&gt; returns JsonElement, which LitJson cannot handle.
    /// </summary>
    private static object? JsonElementToNative(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => JsonElementToNative(p.Value)),
        JsonValueKind.Array => element.EnumerateArray()
            .Select(JsonElementToNative).ToList<object?>(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    /// <summary>Converts an AWS Document back to a JSON string.</summary>
    private static string DocumentToJson(Document doc)
    {
        if (doc.IsDictionary())
        {
            var dict = doc.AsDictionary();
            var jsonDict = new Dictionary<string, object?>();
            foreach (var kvp in dict)
                jsonDict[kvp.Key] = DocumentToObject(kvp.Value);
            return JsonSerializer.Serialize(jsonDict);
        }
        return "{}";
    }

    private static object? DocumentToObject(Document doc)
    {
        if (doc.IsNull()) return null;
        if (doc.IsBool()) return doc.AsBool();
        if (doc.IsInt()) return doc.AsInt();
        if (doc.IsLong()) return doc.AsLong();
        if (doc.IsDouble()) return doc.AsDouble();
        if (doc.IsString()) return doc.AsString();
        if (doc.IsList()) return doc.AsList().Select(DocumentToObject).ToList();
        if (doc.IsDictionary())
        {
            var dict = doc.AsDictionary();
            var result = new Dictionary<string, object?>();
            foreach (var kvp in dict)
                result[kvp.Key] = DocumentToObject(kvp.Value);
            return result;
        }
        return null;
    }
}
