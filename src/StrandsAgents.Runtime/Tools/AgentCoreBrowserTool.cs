using System.Net.Http.Json;
using System.Text.Json;
using StrandsAgents.Core;

namespace StrandsAgents.Runtime.Tools;

/// <summary>
/// Managed browser sandbox via Amazon Bedrock AgentCore Browser.
///
/// <para>
/// Provides access to a managed headless browser that can execute JavaScript,
/// render dynamic pages, and interact with web content. Unlike
/// <see cref="StrandsAgents.Tools.HttpRequestTool"/>, which makes simple HTTP requests,
/// this tool operates a full browser engine and can handle JS-rendered SPAs,
/// authentication flows, and interactive UI elements.
/// </para>
/// </summary>
public sealed class AgentCoreBrowserTool : ITool, IAsyncDisposable
{
    private static readonly ToolDefinition _definition = new(
        Name: "agentcore_browser",
        Description: """
            Operates a managed browser sandbox provided by Amazon Bedrock AgentCore.
            Use this for JS-rendered pages, SPAs, or sites that require interaction.
            For simple HTTP GET/POST requests, use the http_request tool instead.

            Operations:
            - navigate: Load a URL in the browser.
            - screenshot: Capture a screenshot of the current page (returns base64 PNG).
            - extract_text: Extract visible text content from the current page.
            - click: Click an element identified by CSS selector.
            - type: Type text into an input element identified by CSS selector.
            """,
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "operation": {
                  "type": "string",
                  "enum": ["navigate", "screenshot", "extract_text", "click", "type"],
                  "description": "The browser operation to perform."
                },
                "url": {
                  "type": "string",
                  "description": "URL to navigate to. Required for navigate."
                },
                "selector": {
                  "type": "string",
                  "description": "CSS selector for click/type operations."
                },
                "text": {
                  "type": "string",
                  "description": "Text to type. Required for type operation."
                }
              },
              "required": ["operation"]
            }
            """).RootElement.Clone());

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initialises a new <see cref="AgentCoreBrowserTool"/>.
    /// </summary>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="HttpClient"/>. When provided, the tool does
    /// not own the client and will not dispose it. Intended for testing.
    /// </param>
    public AgentCoreBrowserTool(
        string region = "us-east-1",
        HttpClient? clientOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        _ownsClient = clientOverride is null;
        _http = clientOverride ?? new HttpClient
        {
            BaseAddress = new Uri($"https://bedrock-agentcore.{region}.amazonaws.com"),
        };
    }

    /// <inheritdoc/>
    public ToolDefinition Definition => _definition;

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("operation", out var opEl))
            return ToolResult.Failure("agentcore_browser", "Missing required field: operation.");

        var operation = opEl.GetString();

        return operation switch
        {
            "navigate" => await BrowserActionAsync("navigate", input, ct).ConfigureAwait(false),
            "screenshot" => await BrowserActionAsync("screenshot", input, ct).ConfigureAwait(false),
            "extract_text" => await BrowserActionAsync("extract_text", input, ct).ConfigureAwait(false),
            "click" => await BrowserActionAsync("click", input, ct).ConfigureAwait(false),
            "type" => await BrowserActionAsync("type", input, ct).ConfigureAwait(false),
            _ => ToolResult.Failure("agentcore_browser",
                $"Unknown operation '{operation}'. Supported: navigate, screenshot, extract_text, click, type."),
        };
    }

    private async Task<ToolResult> BrowserActionAsync(
        string action, JsonElement input, CancellationToken ct)
    {
        var payload = new
        {
            action,
            url = input.TryGetProperty("url", out var u) ? u.GetString() : null,
            selector = input.TryGetProperty("selector", out var s) ? s.GetString() : null,
            text = input.TryGetProperty("text", out var t) ? t.GetString() : null,
        };

        using var response = await _http
            .PostAsJsonAsync("/browser/actions", payload, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return ToolResult.Failure("agentcore_browser",
                $"Browser action '{action}' failed. Status: {(int)response.StatusCode}");

        var result = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ToolResult.Success("agentcore_browser", result);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
            _http.Dispose();

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}
