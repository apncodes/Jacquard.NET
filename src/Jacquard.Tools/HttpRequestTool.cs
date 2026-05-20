using Jacquard.Core;
using System.Text;
using System.Text.Json;

namespace Jacquard.Tools;

/// <summary>
/// Built-in tool that performs HTTP GET or POST requests.
/// Requires an <see cref="IHttpClientFactory"/> — do not use <c>new HttpClient()</c>.
/// </summary>
public sealed class HttpRequestTool : ITool
{
    /// <summary>Named <see cref="HttpClient"/> key used when registering via <see cref="IHttpClientFactory"/>.</summary>
    public const string HttpClientName = "StrandsHttpRequestTool";

    private static readonly ToolDefinition _definition = new(
        Name: "http_request",
        Description: "Performs an HTTP GET or POST request and returns the response status code and body.",
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "method":  { "type": "string", "enum": ["GET", "POST"], "description": "HTTP method." },
                "url":     { "type": "string", "description": "The full URL to request." },
                "headers": { "type": "object", "description": "Optional HTTP headers as key/value pairs.", "additionalProperties": { "type": "string" } },
                "body":    { "type": "string", "description": "Request body for POST requests." }
              },
              "required": ["method", "url"]
            }
            """).RootElement.Clone());

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new <see cref="HttpRequestTool"/>.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create <see cref="HttpClient"/> instances.</param>
    public HttpRequestTool(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc/>
    public ToolDefinition Definition => _definition;

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("method", out var methodEl) ||
            !input.TryGetProperty("url", out var urlEl))
            return ToolResult.Failure(Definition.Name, "Missing required fields: method, url.");

        var method = methodEl.GetString()?.ToUpperInvariant();
        var url = urlEl.GetString();

        if (string.IsNullOrEmpty(url))
            return ToolResult.Failure(Definition.Name, "url must be a non-empty string.");

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            method == "POST" ? HttpMethod.Post : HttpMethod.Get,
            url);

        // Apply optional headers
        if (input.TryGetProperty("headers", out var headersEl) &&
            headersEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var header in headersEl.EnumerateObject())
                request.Headers.TryAddWithoutValidation(header.Name, header.Value.GetString());
        }

        // Apply optional body for POST
        if (method == "POST" &&
            input.TryGetProperty("body", out var bodyEl) &&
            bodyEl.GetString() is { } body)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Failure(Definition.Name, $"Request failed: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Failure(Definition.Name, "Request timed out or was cancelled.");
        }

        using (response)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var result = JsonSerializer.Serialize(new
            {
                statusCode = (int)response.StatusCode,
                body = responseBody
            });

            return response.IsSuccessStatusCode
                ? ToolResult.Success(Definition.Name, result)
                : ToolResult.Failure(Definition.Name, result);
        }
    }
}
