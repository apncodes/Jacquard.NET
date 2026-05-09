using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StrandsAgents.Core;

/// <summary>
/// Anthropic Claude model provider using the Messages API directly.
/// No third-party SDK required — uses HttpClient + System.Text.Json.
/// </summary>
public sealed class AnthropicModel : IModel, IDisposable
{
    /// <summary>Named <see cref="HttpClient"/> key used when registering via <see cref="System.Net.Http.IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "Anthropic";

    private readonly HttpClient _http;
    private readonly string _modelId;
    private readonly bool _ownsClient;
    private static readonly Uri BaseUri = new("https://api.anthropic.com/v1/messages");

    /// <summary>
    /// Initializes a new <see cref="AnthropicModel"/>.
    /// </summary>
    /// <param name="apiKey">Anthropic API key.</param>
    /// <param name="modelId">Model identifier. Default: "claude-sonnet-4-5".</param>
    /// <param name="httpClientFactory">
    /// Factory used to create the <see cref="HttpClient"/>. When provided, the client is not disposed
    /// by this instance. Preferred in hosted applications to avoid socket exhaustion.
    /// </param>
    /// <param name="httpClient">
    /// An existing <see cref="HttpClient"/> to reuse. When provided, the client is not disposed
    /// by this instance. Useful for testing.
    /// </param>
    public AnthropicModel(
        string apiKey,
        string modelId = "claude-sonnet-4-5",
        IHttpClientFactory? httpClientFactory = null,
        HttpClient? httpClient = null)
    {
        _modelId = modelId;

        if (httpClientFactory is not null)
        {
            _http = httpClientFactory.CreateClient(HttpClientName);
            _ownsClient = false;
        }
        else if (httpClient is not null)
        {
            _http = httpClient;
            _ownsClient = false;
        }
        else
        {
            _http = new HttpClient();
            _ownsClient = true;
        }

        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
    }

    /// <inheritdoc/>
    public async Task<ModelResponse> InvokeAsync(ModelRequest request, CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: false);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(BaseUri, content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelException(
                $"HTTP request to Anthropic API failed: {ex.Message}",
                request,
                httpStatusCode: (int?)ex.StatusCode,
                inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new ModelException(
                    $"Anthropic API returned {(int)response.StatusCode}: {errorBody}",
                    request,
                    httpStatusCode: (int)response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse(json);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: true);
        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, BaseUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        reqMsg.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(reqMsg, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelException(
                $"HTTP request to Anthropic API failed: {ex.Message}",
                request,
                httpStatusCode: (int?)ex.StatusCode,
                inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new ModelException(
                $"Anthropic API returned {statusCode} on stream request.",
                request,
                httpStatusCode: statusCode);
        }

        using var responseOwner = response;
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        string? currentToolId = null;
        string? currentToolName = null;
        var toolInputBuffers = new Dictionary<string, StringBuilder>();
        var toolCalls = new List<ToolCall>();
        string? textContent = null;
        int inputTokens = 0, outputTokens = 0;
        StopReason stopReason = StopReason.EndTurn;

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            JsonNode? node;
            try { node = JsonNode.Parse(data); } catch { continue; }
            if (node is null) continue;

            var type = node["type"]?.GetValue<string>();
            switch (type)
            {
                case "message_start":
                    inputTokens = node["message"]?["usage"]?["input_tokens"]?.GetValue<int>() ?? 0;
                    break;

                case "content_block_start":
                    var blockType = node["content_block"]?["type"]?.GetValue<string>();
                    if (blockType == "tool_use")
                    {
                        currentToolId = node["content_block"]?["id"]?.GetValue<string>();
                        currentToolName = node["content_block"]?["name"]?.GetValue<string>();
                        if (currentToolId is not null)
                        {
                            toolInputBuffers[currentToolId] = new StringBuilder();
                            yield return new ToolCallStartModelEvent(currentToolId, currentToolName ?? "");
                        }
                    }
                    break;

                case "content_block_delta":
                    var deltaType = node["delta"]?["type"]?.GetValue<string>();
                    if (deltaType == "text_delta")
                    {
                        var text = node["delta"]?["text"]?.GetValue<string>() ?? "";
                        textContent = (textContent ?? "") + text;
                        yield return new TextDeltaModelEvent(text);
                    }
                    else if (deltaType == "input_json_delta" && currentToolId is not null)
                    {
                        var partial = node["delta"]?["partial_json"]?.GetValue<string>() ?? "";
                        toolInputBuffers[currentToolId].Append(partial);
                        yield return new ToolCallInputDeltaModelEvent(currentToolId, partial);
                    }
                    break;

                case "content_block_stop":
                    if (currentToolId is not null && currentToolName is not null
                        && toolInputBuffers.TryGetValue(currentToolId, out var inputSb))
                    {
                        var inputJson = inputSb.Length > 0 ? inputSb.ToString() : "{}";
                        toolCalls.Add(new ToolCall(
                            currentToolId,
                            currentToolName,
                            JsonDocument.Parse(inputJson).RootElement));
                    }
                    currentToolId = null;
                    currentToolName = null;
                    break;

                case "message_delta":
                    outputTokens = node["usage"]?["output_tokens"]?.GetValue<int>() ?? outputTokens;
                    var stopReasonStr = node["delta"]?["stop_reason"]?.GetValue<string>();
                    stopReason = MapStopReason(stopReasonStr);
                    break;
            }
        }

        var usage = new TokenUsage(inputTokens, outputTokens);
        var finalResponse = new ModelResponse(textContent, toolCalls, stopReason, usage);
        yield return new ModelCompleteEvent(finalResponse);
    }

    private string BuildRequestBody(ModelRequest request, bool stream)
    {
        var messages = request.Messages.Select(MapMessage).ToList();
        var tools = request.Tools.Select(MapTool).ToList();

        var obj = new JsonObject
        {
            ["model"] = request.Parameters.ModelId ?? _modelId,
            ["max_tokens"] = request.Parameters.MaxTokens ?? 4096,
            ["messages"] = JsonSerializer.SerializeToNode(messages),
            ["stream"] = stream
        };

        if (request.SystemPrompt is not null)
            obj["system"] = request.SystemPrompt;

        if (tools.Count > 0)
            obj["tools"] = JsonSerializer.SerializeToNode(tools);

        if (request.Parameters.Temperature.HasValue)
            obj["temperature"] = request.Parameters.Temperature.Value;

        return obj.ToJsonString();
    }

    private static object MapMessage(Message msg)
    {
        var content = msg.Content.Select(MapContentBlock).ToList();
        return new { role = msg.Role == Role.User ? "user" : "assistant", content };
    }

    private static object MapContentBlock(ContentBlock block) => block switch
    {
        TextBlock t => (object)new { type = "text", text = t.Text },
        ToolUseBlock tu => new { type = "tool_use", id = tu.Id, name = tu.Name, input = JsonSerializer.Deserialize<object>(tu.Input.GetRawText()) },
        ToolResultBlock tr => new { type = "tool_result", tool_use_id = tr.ToolUseId, content = tr.Content, is_error = tr.IsError },
        _ => new { type = "text", text = "" }
    };

    private static object MapTool(ToolDefinition def) => new
    {
        name = def.Name,
        description = def.Description,
        input_schema = JsonSerializer.Deserialize<object>(def.InputSchema.GetRawText())
    };

    private static ModelResponse ParseResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? text = null;
        var toolCalls = new List<ToolCall>();

        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();
            if (type == "text")
                text = block.GetProperty("text").GetString();
            else if (type == "tool_use")
            {
                toolCalls.Add(new ToolCall(
                    block.GetProperty("id").GetString()!,
                    block.GetProperty("name").GetString()!,
                    block.GetProperty("input").Clone()));
            }
        }

        var stopReason = MapStopReason(
            root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null);

        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            inputTokens = usage.GetProperty("input_tokens").GetInt32();
            outputTokens = usage.GetProperty("output_tokens").GetInt32();
        }

        return new ModelResponse(text, toolCalls, stopReason, new TokenUsage(inputTokens, outputTokens));
    }

    private static StopReason MapStopReason(string? reason) => reason switch
    {
        "end_turn" => StopReason.EndTurn,
        "tool_use" => StopReason.ToolUse,
        "max_tokens" => StopReason.MaxTokens,
        "stop_sequence" => StopReason.StopSequence,
        _ => StopReason.EndTurn
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
