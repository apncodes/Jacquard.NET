using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Jacquard.Core;

namespace Jacquard.Runtime.Tools;

/// <summary>
/// Managed browser session management via Amazon Bedrock AgentCore Browser,
/// backed by the official <c>AWSSDK.BedrockAgentCore</c> SDK client.
///
/// <para>
/// AgentCore Browser provides a managed headless Chrome instance. This tool exposes
/// session lifecycle operations (start, get, stop) and surfaces the
/// <c>AutomationStream.StreamEndpoint</c> URL that callers use to connect via
/// Playwright (CDP) or Nova Act for actual browser automation.
/// </para>
///
/// <para>
/// Typical usage pattern:
/// <list type="number">
///   <item>Call <c>start_session</c> to create a browser session. The response includes
///   the <c>automationStreamEndpoint</c> URL.</item>
///   <item>Connect to the endpoint via Playwright (<c>connect_over_cdp</c>) or Nova Act
///   to perform navigation, clicks, screenshots, etc.</item>
///   <item>Call <c>stop_session</c> when done to release resources.</item>
/// </list>
/// </para>
///
/// <para>
/// Authentication is handled automatically by the SDK via the standard AWS credential
/// chain (environment variables, <c>~/.aws/credentials</c>, instance metadata, etc.).
/// </para>
/// </summary>
public sealed class AgentCoreBrowserTool : ITool, IDisposable
{
    private const string ToolId = "agentcore_browser";

    private static readonly ToolDefinition _definition = new(
        Name: ToolId,
        Description: """
            Manages Amazon Bedrock AgentCore Browser sessions.

            AgentCore Browser provides a managed headless Chrome instance. Use start_session
            to create a session and receive the automationStreamEndpoint URL, then connect
            to that endpoint via Playwright (connect_over_cdp) or Nova Act to perform
            browser automation (navigate, click, screenshot, etc.).

            Operations:
            - start_session: Start a new browser session. Returns sessionId and automationStreamEndpoint.
            - get_session:   Get the status and stream endpoint of an existing session.
            - stop_session:  Stop a browser session and release its resources.
            """,
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "operation": {
                  "type": "string",
                  "enum": ["start_session", "get_session", "stop_session"],
                  "description": "The browser session operation to perform."
                },
                "session_id": {
                  "type": "string",
                  "description": "The browser session ID. Required for get_session and stop_session."
                },
                "session_timeout_seconds": {
                  "type": "integer",
                  "description": "Session timeout in seconds for start_session. Default: 3600 (1 hour)."
                }
              },
              "required": ["operation"]
            }
            """).RootElement.Clone());

    private readonly IAmazonBedrockAgentCore _client;
    private readonly string _browserIdentifier;
    private readonly bool _ownsClient;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Initialises a new <see cref="AgentCoreBrowserTool"/>.
    /// </summary>
    /// <param name="browserIdentifier">
    /// The AgentCore Browser resource identifier. When <c>null</c>, the SDK uses the
    /// default browser for the account.
    /// </param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="IAmazonBedrockAgentCore"/> client. When provided,
    /// the tool does not own the client and will not dispose it. Intended for testing.
    /// </param>
    public AgentCoreBrowserTool(
        string? browserIdentifier = null,
        string region = "us-east-1",
        IAmazonBedrockAgentCore? clientOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        _browserIdentifier = browserIdentifier ?? "default";
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
            return ToolResult.Failure(ToolId, "Missing required field: operation.");

        var operation = opEl.GetString();

        return operation switch
        {
            "start_session" => await HandleStartSessionAsync(input, ct).ConfigureAwait(false),
            "get_session"   => await HandleGetSessionAsync(input, ct).ConfigureAwait(false),
            "stop_session"  => await HandleStopSessionAsync(input, ct).ConfigureAwait(false),
            _ => ToolResult.Failure(ToolId,
                $"Unknown operation '{operation}'. Supported: start_session, get_session, stop_session."),
        };
    }

    // ── start_session ─────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleStartSessionAsync(JsonElement input, CancellationToken ct)
    {
        var timeoutSeconds = 3600;
        if (input.TryGetProperty("session_timeout_seconds", out var timeoutEl) &&
            timeoutEl.ValueKind == JsonValueKind.Number)
            timeoutSeconds = timeoutEl.GetInt32();

        var request = new StartBrowserSessionRequest
        {
            BrowserIdentifier = _browserIdentifier,
            Name = $"strands-browser-{Guid.NewGuid():N}"[..40],
            SessionTimeoutSeconds = timeoutSeconds,
        };

        try
        {
            var response = await _client.StartBrowserSessionAsync(request, ct)
                .ConfigureAwait(false);

            var result = new
            {
                sessionId = response.SessionId,
                automationStreamEndpoint = response.Streams?.AutomationStream?.StreamEndpoint,
                status = "STARTED",
                message = "Connect to automationStreamEndpoint via Playwright (connect_over_cdp) or Nova Act.",
            };

            return ToolResult.Success(ToolId, JsonSerializer.Serialize(result, _json));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure(ToolId, $"start_session failed: {ex.Message}");
        }
    }

    // ── get_session ───────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleGetSessionAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("session_id", out var idEl) ||
            idEl.GetString() is not { Length: > 0 } sessionId)
            return ToolResult.Failure(ToolId, "session_id is required for get_session.");

        var request = new GetBrowserSessionRequest
        {
            BrowserIdentifier = _browserIdentifier,
            SessionId = sessionId,
        };

        try
        {
            var response = await _client.GetBrowserSessionAsync(request, ct)
                .ConfigureAwait(false);

            var result = new
            {
                sessionId = response.SessionId,
                status = response.Status?.Value ?? "UNKNOWN",
                automationStreamEndpoint = response.Streams?.AutomationStream?.StreamEndpoint,
            };

            return ToolResult.Success(ToolId, JsonSerializer.Serialize(result, _json));
        }
        catch (Amazon.BedrockAgentCore.Model.ResourceNotFoundException)
        {
            return ToolResult.Failure(ToolId, $"No browser session found with id: {sessionId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure(ToolId, $"get_session failed: {ex.Message}");
        }
    }

    // ── stop_session ──────────────────────────────────────────────────────────

    private async Task<ToolResult> HandleStopSessionAsync(JsonElement input, CancellationToken ct)
    {
        if (!input.TryGetProperty("session_id", out var idEl) ||
            idEl.GetString() is not { Length: > 0 } sessionId)
            return ToolResult.Failure(ToolId, "session_id is required for stop_session.");

        var request = new StopBrowserSessionRequest
        {
            BrowserIdentifier = _browserIdentifier,
            SessionId = sessionId,
        };

        try
        {
            await _client.StopBrowserSessionAsync(request, ct).ConfigureAwait(false);
            return ToolResult.Success(ToolId, $"Browser session stopped: {sessionId}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolResult.Failure(ToolId, $"stop_session failed: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }
}
