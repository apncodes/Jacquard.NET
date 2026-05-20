using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jacquard.Core;

/// <summary>
/// OpenAI-compatible model provider using the Chat Completions API.
/// Works with OpenAI, Azure OpenAI, Ollama, LM Studio, and any OpenAI-compatible endpoint.
/// No third-party SDK required — uses HttpClient + System.Text.Json.
/// </summary>
public sealed class OpenAICompatibleModel : IModel, IDisposable
{
    /// <summary>Named <see cref="HttpClient"/> key used when registering via <see cref="System.Net.Http.IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "OpenAICompatible";

    private readonly HttpClient _http;
    private readonly string _modelId;
    private readonly Uri _endpoint;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initializes a new <see cref="OpenAICompatibleModel"/>.
    /// </summary>
    /// <param name="baseUrl">Base URL of the OpenAI-compatible endpoint (e.g. "https://api.openai.com/v1").</param>
    /// <param name="apiKey">API key sent as a Bearer token. Pass an empty string for unauthenticated endpoints.</param>
    /// <param name="modelId">Model identifier to use in requests. Default: "gpt-4o".</param>
    /// <param name="httpClientFactory">
    /// Factory used to create the <see cref="HttpClient"/>. When provided, the client is not disposed
    /// by this instance. Preferred in hosted applications to avoid socket exhaustion.
    /// </param>
    /// <param name="httpClient">
    /// An existing <see cref="HttpClient"/> to reuse. When provided, the client is not disposed
    /// by this instance. Useful for testing.
    /// </param>
    public OpenAICompatibleModel(
        string baseUrl,
        string apiKey,
        string modelId = "gpt-4o",
        IHttpClientFactory? httpClientFactory = null,
        HttpClient? httpClient = null)
    {
        _modelId = modelId;
        _endpoint = new Uri(baseUrl.TrimEnd('/') + "/chat/completions");

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

        if (!string.IsNullOrEmpty(apiKey))
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <inheritdoc/>
    public async Task<ModelResponse> InvokeAsync(ModelRequest request, CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: false);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_endpoint, content, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelException(
                $"HTTP request to OpenAI-compatible endpoint failed: {ex.Message}",
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
                    $"OpenAI-compatible endpoint returned {(int)response.StatusCode}: {errorBody}",
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
        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, _endpoint)
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
                $"HTTP request to OpenAI-compatible endpoint failed: {ex.Message}",
                request,
                httpStatusCode: (int?)ex.StatusCode,
                inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new ModelException(
                $"OpenAI-compatible endpoint returned {statusCode} on stream request.",
                request,
                httpStatusCode: statusCode);
        }

        using var responseOwner = response;
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream);

        // Accumulate streaming state
        string? textContent = null;
        StopReason stopReason = StopReason.EndTurn;
        int inputTokens = 0, outputTokens = 0;

        // tool_calls accumulation by index
        var toolCallIds = new Dictionary<int, string>();
        var toolCallNames = new Dictionary<int, string>();
        var toolCallArgBuffers = new Dictionary<int, StringBuilder>();

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null || !line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            JsonNode? node;
            try { node = JsonNode.Parse(data); } catch { continue; }
            if (node is null) continue;

            var choices = node["choices"]?.AsArray();
            if (choices is null || choices.Count == 0) continue;

            var choice = choices[0];
            var delta = choice?["delta"];
            if (delta is null) continue;

            // Text delta
            var textDelta = delta["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(textDelta))
            {
                textContent = (textContent ?? "") + textDelta;
                yield return new TextDeltaModelEvent(textDelta);
            }

            // Tool call deltas (accumulated by index)
            var toolCallsArray = delta["tool_calls"]?.AsArray();
            if (toolCallsArray is not null)
            {
                foreach (var tc in toolCallsArray)
                {
                    if (tc is null) continue;
                    var idx = tc["index"]?.GetValue<int>() ?? 0;

                    // Capture id and name on first chunk for this index
                    var tcId = tc["id"]?.GetValue<string>();
                    var tcName = tc["function"]?["name"]?.GetValue<string>();
                    var argsDelta = tc["function"]?["arguments"]?.GetValue<string>() ?? "";

                    if (!toolCallArgBuffers.ContainsKey(idx))
                    {
                        toolCallArgBuffers[idx] = new StringBuilder();
                        toolCallIds[idx] = tcId ?? $"call_{idx}";
                        toolCallNames[idx] = tcName ?? "";
                        yield return new ToolCallStartModelEvent(toolCallIds[idx], toolCallNames[idx]);
                    }
                    else
                    {
                        // Update name if we get it in a later chunk (some providers split it)
                        if (!string.IsNullOrEmpty(tcName))
                            toolCallNames[idx] = tcName;
                    }

                    if (!string.IsNullOrEmpty(argsDelta))
                    {
                        toolCallArgBuffers[idx].Append(argsDelta);
                        yield return new ToolCallInputDeltaModelEvent(toolCallIds[idx], argsDelta);
                    }
                }
            }

            // finish_reason
            var finishReason = choice?["finish_reason"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(finishReason))
                stopReason = MapStopReason(finishReason);

            // Usage (some providers send it on the last chunk)
            var usageNode = node["usage"];
            if (usageNode is not null)
            {
                inputTokens = usageNode["prompt_tokens"]?.GetValue<int>() ?? inputTokens;
                outputTokens = usageNode["completion_tokens"]?.GetValue<int>() ?? outputTokens;
            }
        }

        // Build final tool calls list
        var toolCalls = new List<ToolCall>();
        foreach (var idx in toolCallArgBuffers.Keys.OrderBy(k => k))
        {
            var argsJson = toolCallArgBuffers[idx].Length > 0
                ? toolCallArgBuffers[idx].ToString()
                : "{}";
            toolCalls.Add(new ToolCall(
                toolCallIds[idx],
                toolCallNames[idx],
                JsonDocument.Parse(argsJson).RootElement));
        }

        var usage = new TokenUsage(inputTokens, outputTokens);
        var finalResponse = new ModelResponse(textContent, toolCalls, stopReason, usage);
        yield return new ModelCompleteEvent(finalResponse);
    }

    private string BuildRequestBody(ModelRequest request, bool stream)
    {
        var messages = new JsonArray();

        // Prepend system prompt
        if (request.SystemPrompt is not null)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }

        // Map conversation messages
        foreach (var msg in request.Messages)
        {
            foreach (var node in MapMessage(msg))
                messages.Add(node);
        }

        var obj = new JsonObject
        {
            ["model"] = request.Parameters.ModelId ?? _modelId,
            ["messages"] = messages,
            ["stream"] = stream
        };

        if (request.Parameters.MaxTokens.HasValue)
            obj["max_tokens"] = request.Parameters.MaxTokens.Value;

        if (request.Parameters.Temperature.HasValue)
            obj["temperature"] = (double)request.Parameters.Temperature.Value;

        var tools = request.Tools.Select(MapTool).ToList();
        if (tools.Count > 0)
        {
            var toolsArray = new JsonArray();
            foreach (var t in tools)
                toolsArray.Add(t);
            obj["tools"] = toolsArray;
        }

        return obj.ToJsonString();
    }

    /// <summary>
    /// Maps a Jacquard message to one or more OpenAI message objects.
    /// ToolResultBlock becomes a separate "tool" role message.
    /// </summary>
    private static IEnumerable<JsonObject> MapMessage(Message msg)
    {
        // Collect text and tool_use blocks for the assistant/user message
        var textParts = new List<string>();
        var toolCallNodes = new JsonArray();
        var toolResultMessages = new List<JsonObject>();

        foreach (var block in msg.Content)
        {
            switch (block)
            {
                case TextBlock t:
                    textParts.Add(t.Text);
                    break;

                case ToolUseBlock tu:
                    toolCallNodes.Add(new JsonObject
                    {
                        ["id"] = tu.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = tu.Name,
                            ["arguments"] = tu.Input.GetRawText()
                        }
                    });
                    break;

                case ToolResultBlock tr:
                    toolResultMessages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = tr.ToolUseId,
                        ["content"] = tr.Content
                    });
                    break;
            }
        }

        // Emit the primary message if it has text or tool_calls
        bool hasText = textParts.Count > 0;
        bool hasToolCalls = toolCallNodes.Count > 0;

        if (hasText || hasToolCalls)
        {
            var role = msg.Role == Role.User ? "user" : "assistant";
            var msgObj = new JsonObject { ["role"] = role };

            if (hasText)
                msgObj["content"] = string.Join("", textParts);

            if (hasToolCalls)
                msgObj["tool_calls"] = toolCallNodes;

            yield return msgObj;
        }

        // Emit tool result messages
        foreach (var tr in toolResultMessages)
            yield return tr;
    }

    private static JsonObject MapTool(ToolDefinition def)
    {
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = def.Name,
                ["description"] = def.Description,
                ["parameters"] = JsonNode.Parse(def.InputSchema.GetRawText())
            }
        };
    }

    private static ModelResponse ParseResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? text = null;
        var toolCalls = new List<ToolCall>();

        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message");

            if (message.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind != JsonValueKind.Null)
                text = contentEl.GetString();

            if (message.TryGetProperty("tool_calls", out var tcArray))
            {
                foreach (var tc in tcArray.EnumerateArray())
                {
                    var id = tc.GetProperty("id").GetString()!;
                    var name = tc.GetProperty("function").GetProperty("name").GetString()!;
                    var argsRaw = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}";
                    toolCalls.Add(new ToolCall(id, name, JsonDocument.Parse(argsRaw).RootElement));
                }
            }
        }

        StopReason stopReason = StopReason.EndTurn;
        if (choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("finish_reason", out var fr) &&
            fr.ValueKind != JsonValueKind.Null)
        {
            stopReason = MapStopReason(fr.GetString());
        }

        int inputTokens = 0, outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct2)) outputTokens = ct2.GetInt32();
        }

        return new ModelResponse(text, toolCalls, stopReason, new TokenUsage(inputTokens, outputTokens));
    }

    private static StopReason MapStopReason(string? reason) => reason switch
    {
        "stop" => StopReason.EndTurn,
        "tool_calls" => StopReason.ToolUse,
        "length" => StopReason.MaxTokens,
        _ => StopReason.EndTurn
    };

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
