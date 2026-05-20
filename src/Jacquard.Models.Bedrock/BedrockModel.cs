using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using Amazon.Runtime.Documents;
using Amazon.Runtime.EventStreams.Internal;
using Polly;
using Polly.Retry;
using System.Text.Json;
using System.Threading.Channels;

namespace Jacquard.Models.Bedrock;

/// <summary>
/// Amazon Bedrock model provider using the Converse API.
/// Default model: us.anthropic.claude-sonnet-4-20250514-v1:0 (cross-region inference profile).
/// Use a cross-region profile ID (e.g. us.anthropic.claude-*) — bare model IDs require
/// on-demand throughput which is not available by default.
/// Retries up to 3 times with exponential backoff and jitter on throttling errors.
/// </summary>
public sealed class BedrockModel : Jacquard.Core.IModel, Jacquard.Core.IGuardrailEvaluator
{
    private readonly IAmazonBedrockRuntime _client;
    private readonly string _modelId;
    private readonly ResiliencePipeline _retryPipeline;
    private readonly BedrockGuardrailConfig? _guardrailConfig;
    private readonly Jacquard.Core.HookRegistry? _hooks;

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
    /// <param name="guardrailConfig">Optional guardrail configuration. When non-null, guardrail evaluation is applied to every model call.</param>
    /// <param name="hooks">Optional hook registry for firing <see cref="Jacquard.Core.GuardrailViolationEvent"/> on violations.</param>
    public BedrockModel(
        string region = "us-east-1",
        string modelId = "us.anthropic.claude-haiku-4-5-20251001-v1:0",
        AmazonBedrockRuntimeConfig? config = null,
        IAmazonBedrockRuntime? clientOverride = null,
        BedrockGuardrailConfig? guardrailConfig = null,
        Jacquard.Core.HookRegistry? hooks = null)
    {
        _modelId = modelId;
        _guardrailConfig = guardrailConfig;
        _hooks = hooks;

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

    // ── IGuardrailEvaluator ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsEnabled => _guardrailConfig is not null;

    /// <inheritdoc/>
    public bool ShadowMode => _guardrailConfig?.ShadowMode ?? false;

    /// <inheritdoc/>
    public async Task<Jacquard.Core.GuardrailEvaluationResult> EvaluateAsync(
        string content,
        string source,
        CancellationToken ct = default)
    {
        if (_guardrailConfig is null)
            return new Jacquard.Core.GuardrailEvaluationResult(
                Jacquard.Core.GuardrailAction.None, null, null, null);

        var request = new ApplyGuardrailRequest
        {
            GuardrailIdentifier = _guardrailConfig.GuardrailId,
            GuardrailVersion = _guardrailConfig.GuardrailVersion,
            Source = source == "INPUT"
                ? GuardrailContentSource.INPUT
                : GuardrailContentSource.OUTPUT,
            Content = [new GuardrailContentBlock
            {
                Text = new GuardrailTextBlock { Text = content }
            }]
        };

        var response = await _client.ApplyGuardrailAsync(request, ct).ConfigureAwait(false);

        var action = response.Action == GuardrailAction.GUARDRAIL_INTERVENED
            ? Jacquard.Core.GuardrailAction.Intervened
            : Jacquard.Core.GuardrailAction.None;

        // Extract the canned blocked message from outputs if present
        string? blockedMessage = null;
        if (response.Outputs?.Count > 0)
            blockedMessage = response.Outputs[0].Text;

        return new Jacquard.Core.GuardrailEvaluationResult(
            action,
            blockedMessage,
            _guardrailConfig.GuardrailId,
            _guardrailConfig.GuardrailVersion);
    }

    // ── IModel ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Jacquard.Core.ModelResponse> InvokeAsync(
        Jacquard.Core.ModelRequest request,
        CancellationToken ct = default)
    {
        // Shadow mode: evaluate content without blocking
        if (_guardrailConfig?.ShadowMode == true)
        {
            try
            {
                var outboundContent = string.Join(" ", request.Messages
                    .Where(m => m.Role == Jacquard.Core.Role.User)
                    .SelectMany(m => m.Content.OfType<Jacquard.Core.TextBlock>())
                    .Select(b => b.Text));

                if (!string.IsNullOrEmpty(outboundContent))
                {
                    var evalResult = await EvaluateAsync(outboundContent, "INPUT", ct).ConfigureAwait(false);
                    if (evalResult.Action != Jacquard.Core.GuardrailAction.None && _hooks is not null)
                    {
                        var violationEvt = new Jacquard.Core.GuardrailViolationEvent(
                            evalResult.GuardrailId ?? string.Empty,
                            evalResult.GuardrailVersion ?? string.Empty,
                            evalResult.Action,
                            Jacquard.Core.GuardrailSource.Input,
                            evalResult.BlockedMessage);
                        await _hooks.FireAsync(violationEvt, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BedrockModel] Shadow mode evaluation failed: {ex.Message}");
            }
        }

        var converseRequest = BuildConverseRequest(request);

        var response = await _retryPipeline.ExecuteAsync(
            async token => await _client.ConverseAsync(converseRequest, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        return MapResponse(response);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<Jacquard.Core.ModelStreamEvent> StreamAsync(
        Jacquard.Core.ModelRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Shadow mode: evaluate content without blocking
        if (_guardrailConfig?.ShadowMode == true)
        {
            try
            {
                var outboundContent = string.Join(" ", request.Messages
                    .Where(m => m.Role == Jacquard.Core.Role.User)
                    .SelectMany(m => m.Content.OfType<Jacquard.Core.TextBlock>())
                    .Select(b => b.Text));

                if (!string.IsNullOrEmpty(outboundContent))
                {
                    var evalResult = await EvaluateAsync(outboundContent, "INPUT", ct).ConfigureAwait(false);
                    if (evalResult.Action != Jacquard.Core.GuardrailAction.None && _hooks is not null)
                    {
                        var violationEvt = new Jacquard.Core.GuardrailViolationEvent(
                            evalResult.GuardrailId ?? string.Empty,
                            evalResult.GuardrailVersion ?? string.Empty,
                            evalResult.Action,
                            Jacquard.Core.GuardrailSource.Input,
                            evalResult.BlockedMessage);
                        await _hooks.FireAsync(violationEvt, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BedrockModel] Shadow mode evaluation failed: {ex.Message}");
            }
        }

        var converseRequest = BuildConverseRequest(request);
        var streamRequest = new ConverseStreamRequest
        {
            ModelId = converseRequest.ModelId,
            Messages = converseRequest.Messages,
            System = converseRequest.System,
            ToolConfig = converseRequest.ToolConfig,
            InferenceConfig = converseRequest.InferenceConfig
        };

        // Add guardrail stream config if configured
        if (_guardrailConfig is not null)
        {
            streamRequest.GuardrailConfig = new GuardrailStreamConfiguration
            {
                GuardrailIdentifier = _guardrailConfig.GuardrailId,
                GuardrailVersion = _guardrailConfig.GuardrailVersion,
                Trace = _guardrailConfig.Trace ? GuardrailTrace.Enabled : GuardrailTrace.Disabled,
                StreamProcessingMode = _guardrailConfig.StreamProcessingMode == GuardrailStreamProcessingMode.Synchronous
                    ? Amazon.BedrockRuntime.GuardrailStreamProcessingMode.Sync
                    : Amazon.BedrockRuntime.GuardrailStreamProcessingMode.Async
            };
        }

        // Retry only covers the initial stream establishment — not mid-stream events.
        var response = await _retryPipeline.ExecuteAsync(
            async token => await _client.ConverseStreamAsync(streamRequest, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);

        string? currentToolId = null;
        string? currentToolName = null;
        string? textContent = null;
        var toolInputBuffers = new Dictionary<string, System.Text.StringBuilder>();
        var toolCalls = new List<Jacquard.Core.ToolCall>();
        Jacquard.Core.TokenUsage usage = Jacquard.Core.TokenUsage.Zero;
        Jacquard.Core.StopReason stopReason = Jacquard.Core.StopReason.EndTurn;

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
                    yield return new Jacquard.Core.TextDeltaModelEvent(delta.Delta.Text);
                    break;

                case ContentBlockStartEvent start when start.Start?.ToolUse is not null:
                    currentToolId = start.Start.ToolUse.ToolUseId;
                    currentToolName = start.Start.ToolUse.Name;
                    toolInputBuffers[currentToolId!] = new System.Text.StringBuilder();
                    yield return new Jacquard.Core.ToolCallStartModelEvent(currentToolId!, currentToolName!);
                    break;

                case ContentBlockDeltaEvent toolDelta when toolDelta.Delta?.ToolUse?.Input is not null:
                    if (currentToolId is not null && toolInputBuffers.TryGetValue(currentToolId, out var sb))
                    {
                        sb.Append(toolDelta.Delta.ToolUse.Input);
                        yield return new Jacquard.Core.ToolCallInputDeltaModelEvent(currentToolId, toolDelta.Delta.ToolUse.Input);
                    }
                    break;

                case ContentBlockStopEvent when currentToolId is not null && currentToolName is not null:
                    if (toolInputBuffers.TryGetValue(currentToolId, out var inputSb))
                    {
                        var inputJson = inputSb.Length > 0 ? inputSb.ToString() : "{}";
                        toolCalls.Add(new Jacquard.Core.ToolCall(
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
                    usage = new Jacquard.Core.TokenUsage(
                        meta.Usage?.InputTokens ?? 0,
                        meta.Usage?.OutputTokens ?? 0);
                    break;
            }
        }

        // Propagate any exception thrown by the background stream reader.
        await fillTask.ConfigureAwait(false);

        var finalResponse = new Jacquard.Core.ModelResponse(textContent, toolCalls, stopReason, usage);
        yield return new Jacquard.Core.ModelCompleteEvent(finalResponse);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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

    private ConverseRequest BuildConverseRequest(Jacquard.Core.ModelRequest request)
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

        var converseRequest = new ConverseRequest
        {
            ModelId = request.Parameters.ModelId ?? _modelId,
            Messages = messages,
            System = system,
            ToolConfig = toolConfig,
            InferenceConfig = inferenceConfig
        };

        // Add guardrail config if configured
        if (_guardrailConfig is not null)
        {
            converseRequest.GuardrailConfig = new GuardrailConfiguration
            {
                GuardrailIdentifier = _guardrailConfig.GuardrailId,
                GuardrailVersion = _guardrailConfig.GuardrailVersion,
                Trace = _guardrailConfig.Trace ? GuardrailTrace.Enabled : GuardrailTrace.Disabled
            };

            // Wrap only the last user message in guardContent when EvaluateLatestMessageOnly=true
            if (_guardrailConfig.EvaluateLatestMessageOnly && converseRequest.Messages.Count > 0)
            {
                var lastUserMsg = converseRequest.Messages.LastOrDefault(m => m.Role == ConversationRole.User);
                if (lastUserMsg is not null)
                {
                    var newContent = new List<ContentBlock>();
                    foreach (var block in lastUserMsg.Content)
                    {
                        if (block.Text is not null)
                        {
                            newContent.Add(new ContentBlock
                            {
                                GuardContent = new GuardrailConverseContentBlock
                                {
                                    Text = new GuardrailConverseTextBlock { Text = block.Text }
                                }
                            });
                        }
                        else
                        {
                            newContent.Add(block);
                        }
                    }
                    lastUserMsg.Content = newContent;
                }
            }
        }

        return converseRequest;
    }

    private static Amazon.BedrockRuntime.Model.Message MapMessage(Jacquard.Core.Message msg)
    {
        var blocks = msg.Content.Select(MapContentBlock).ToList();
        return new Amazon.BedrockRuntime.Model.Message
        {
            Role = msg.Role == Jacquard.Core.Role.User ? ConversationRole.User : ConversationRole.Assistant,
            Content = blocks
        };
    }

    private static ContentBlock MapContentBlock(Jacquard.Core.ContentBlock block) => block switch
    {
        Jacquard.Core.TextBlock t => new ContentBlock { Text = t.Text },
        Jacquard.Core.ToolUseBlock tu => new ContentBlock
        {
            ToolUse = new ToolUseBlock
            {
                ToolUseId = tu.Id,
                Name = tu.Name,
                Input = JsonElementToDocument(tu.Input)
            }
        },
        Jacquard.Core.ToolResultBlock tr => new ContentBlock
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

    private static Tool MapTool(Jacquard.Core.ToolDefinition def)
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

    private Jacquard.Core.ModelResponse MapResponse(ConverseResponse response)
    {
        string? text = null;
        var toolCalls = new List<Jacquard.Core.ToolCall>();

        foreach (var block in response.Output?.Message?.Content ?? new List<ContentBlock>())
        {
            if (block.Text is not null)
                text = block.Text;
            if (block.ToolUse is not null)
            {
                var inputJson = DocumentToJson(block.ToolUse.Input);
                toolCalls.Add(new Jacquard.Core.ToolCall(
                    block.ToolUse.ToolUseId,
                    block.ToolUse.Name,
                    JsonDocument.Parse(inputJson).RootElement));
            }
        }

        var usage = new Jacquard.Core.TokenUsage(
            response.Usage?.InputTokens ?? 0,
            response.Usage?.OutputTokens ?? 0);

        var stopReason = MapStopReason(response.StopReason);

        // Handle guardrail intervention
        if (stopReason == Jacquard.Core.StopReason.GuardrailBlocked && _guardrailConfig is not null)
        {
            if (_guardrailConfig.RedactOutput)
            {
                // Redact mode: suppress the original response entirely.
                // Use the explicit override message if set, otherwise the hardcoded placeholder.
                // Never surface the original model output when redaction is enabled.
                text = _guardrailConfig.RedactOutputMessage ?? "[Output redacted by guardrail]";
            }
            else
            {
                // Not redacting — surface Bedrock's blocked message from the response if available,
                // falling back to whatever text the model returned.
                var bedrockBlockedMessage = response.Output?.Message?.Content
                    ?.FirstOrDefault(b => b.Text is not null)?.Text;
                text ??= bedrockBlockedMessage;
            }
        }

        return new Jacquard.Core.ModelResponse(text, toolCalls, stopReason, usage);
    }

    private static Jacquard.Core.StopReason MapStopReason(Amazon.BedrockRuntime.StopReason? reason)
    {
        if (reason == Amazon.BedrockRuntime.StopReason.Tool_use)
            return Jacquard.Core.StopReason.ToolUse;
        if (reason == Amazon.BedrockRuntime.StopReason.Max_tokens)
            return Jacquard.Core.StopReason.MaxTokens;
        if (reason == Amazon.BedrockRuntime.StopReason.Stop_sequence)
            return Jacquard.Core.StopReason.StopSequence;
        if (reason == Amazon.BedrockRuntime.StopReason.Guardrail_intervened)
            return Jacquard.Core.StopReason.GuardrailBlocked;
        return Jacquard.Core.StopReason.EndTurn;
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
            // Build JSON manually to avoid reflection-based JsonSerializer in AOT contexts.
            var sb = new System.Text.StringBuilder("{");
            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"');
                sb.Append(kvp.Key.Replace("\"", "\\\""));
                sb.Append("\":");
                sb.Append(DocumentToJsonValue(kvp.Value));
            }
            sb.Append('}');
            return sb.ToString();
        }
        return "{}";
    }

    private static string DocumentToJsonValue(Document doc)
    {
        if (doc.IsNull()) return "null";
        if (doc.IsBool()) return doc.AsBool() ? "true" : "false";
        if (doc.IsInt()) return doc.AsInt().ToString();
        if (doc.IsLong()) return doc.AsLong().ToString();
        if (doc.IsDouble()) return doc.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (doc.IsString()) return "\"" + doc.AsString().Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
        if (doc.IsList())
        {
            var sb = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (var item in doc.AsList())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(DocumentToJsonValue(item));
            }
            sb.Append(']');
            return sb.ToString();
        }
        if (doc.IsDictionary()) return DocumentToJson(doc);
        return "null";
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
