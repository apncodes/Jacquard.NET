using System.Text.Json;
using Amazon.BedrockAgentCore;
using Amazon.BedrockAgentCore.Model;
using Jacquard.Core;

namespace Jacquard.Runtime.Tools;

/// <summary>
/// Managed code execution sandbox via Amazon Bedrock AgentCore Code Interpreter,
/// backed by the official <c>AWSSDK.BedrockAgentCore</c> SDK client.
///
/// <para>
/// Manages a code interpreter session lifecycle (start on first use, reuse across calls,
/// stop on dispose) and executes code via <c>InvokeCodeInterpreterAsync</c>.
/// The session is stateful — variables and state persist across calls within the same
/// tool instance lifetime.
/// </para>
///
/// <para>
/// Supported languages: <c>python</c>, <c>javascript</c>, <c>typescript</c>.
/// </para>
///
/// <para>
/// Authentication is handled automatically by the SDK via the standard AWS credential
/// chain (environment variables, <c>~/.aws/credentials</c>, instance metadata, etc.).
/// </para>
/// </summary>
public sealed class AgentCoreCodeInterpreterTool : ITool, IAsyncDisposable
{
    private const string ToolId = "agentcore_code_interpreter";

    private static readonly ToolDefinition _definition = new(
        Name: ToolId,
        Description: """
            Executes code in a managed sandbox provided by Amazon Bedrock AgentCore Code Interpreter.
            The session is stateful — variables and imports persist across calls within the same session.
            Returns stdout, stderr, exit code, and execution time.
            Supported languages: python, javascript, typescript.
            """,
        InputSchema: JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "code": {
                  "type": "string",
                  "description": "The source code to execute."
                },
                "language": {
                  "type": "string",
                  "enum": ["python", "javascript", "typescript"],
                  "description": "Programming language of the code."
                },
                "clear_context": {
                  "type": "boolean",
                  "description": "When true, clears the session context before executing (resets variables). Default: false."
                }
              },
              "required": ["code", "language"]
            }
            """).RootElement.Clone());

    private readonly IAmazonBedrockAgentCore _client;
    private readonly string _codeInterpreterIdentifier;
    private readonly bool _ownsClient;

    // Session is created lazily on first invoke and reused across calls.
    private string? _sessionId;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    /// <summary>
    /// Initialises a new <see cref="AgentCoreCodeInterpreterTool"/>.
    /// </summary>
    /// <param name="codeInterpreterIdentifier">
    /// The AgentCore Code Interpreter resource identifier. When <c>null</c>, the SDK
    /// uses the default code interpreter for the account.
    /// </param>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="IAmazonBedrockAgentCore"/> client. When provided,
    /// the tool does not own the client and will not dispose it. Intended for testing.
    /// </param>
    public AgentCoreCodeInterpreterTool(
        string? codeInterpreterIdentifier = null,
        string region = "us-east-1",
        IAmazonBedrockAgentCore? clientOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);

        _codeInterpreterIdentifier = codeInterpreterIdentifier ?? "default";
        _ownsClient = clientOverride is null;
        _client = clientOverride ?? new AmazonBedrockAgentCoreClient(
            Amazon.RegionEndpoint.GetBySystemName(region));
    }

    /// <inheritdoc/>
    public ToolDefinition Definition => _definition;

    /// <inheritdoc/>
    public async Task<ToolResult> InvokeAsync(JsonElement input, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("code", out var codeEl) ||
            codeEl.GetString() is not { Length: > 0 } code)
            return ToolResult.Failure(ToolId, "code is required and must be non-empty.");

        if (!input.TryGetProperty("language", out var langEl) ||
            langEl.GetString() is not { Length: > 0 } languageStr)
            return ToolResult.Failure(ToolId, "language is required. Supported: python, javascript, typescript.");

        var language = languageStr.ToLowerInvariant() switch
        {
            "python"     => ProgrammingLanguage.Python,
            "javascript" => ProgrammingLanguage.Javascript,
            "typescript" => ProgrammingLanguage.Typescript,
            _ => (ProgrammingLanguage?)null,
        };

        if (language is null)
            return ToolResult.Failure(ToolId,
                $"Unsupported language '{languageStr}'. Supported: python, javascript, typescript.");

        var clearContext = input.TryGetProperty("clear_context", out var clearEl)
            && clearEl.ValueKind == JsonValueKind.True;

        try
        {
            var sessionId = await EnsureSessionAsync(ct).ConfigureAwait(false);

            var request = new InvokeCodeInterpreterRequest
            {
                CodeInterpreterIdentifier = _codeInterpreterIdentifier,
                SessionId = sessionId,
                Name = "executeCode",
                Arguments = new ToolArguments
                {
                    Code = code,
                    Language = language,
                    ClearContext = clearContext,
                },
            };

            var response = await _client.InvokeCodeInterpreterAsync(request, ct)
                .ConfigureAwait(false);

            // The response is an event stream — subscribe to ResultReceived and process.
            CodeInterpreterResult? result = null;
            var tcs = new TaskCompletionSource<CodeInterpreterResult?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            response.Stream.ResultReceived += (_, args) =>
                tcs.TrySetResult(args.EventStreamEvent);

            response.Stream.ExceptionReceived += (_, args) =>
                tcs.TrySetException(args.EventStreamException);

            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));

            await response.Stream.StartProcessingAsync().ConfigureAwait(false);
            result = await tcs.Task.ConfigureAwait(false);

            if (result is null)
                return ToolResult.Failure(ToolId, "Code interpreter returned no result.");

            var structured = result.StructuredContent;
            var textContent = result.Content?
                .Where(c => c.Type?.Value == "text")
                .Select(c => c.Text)
                .FirstOrDefault() ?? string.Empty;

            var summary = structured is not null
                ? $"Exit code: {structured.ExitCode}\n" +
                  $"Execution time: {structured.ExecutionTime:F3}s\n\n" +
                  (string.IsNullOrEmpty(structured.Stdout) ? "" : $"Stdout:\n{structured.Stdout}\n\n") +
                  (string.IsNullOrEmpty(structured.Stderr) ? "" : $"Stderr:\n{structured.Stderr}")
                : textContent;

            return result.IsError
                ? ToolResult.Failure(ToolId, summary.TrimEnd())
                : ToolResult.Success(ToolId, summary.TrimEnd());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // If the session expired, clear it so the next call starts a fresh one.
            if (ex.Message.Contains("session", StringComparison.OrdinalIgnoreCase))
                _sessionId = null;

            return ToolResult.Failure(ToolId, $"Code execution failed: {ex.Message}");
        }
    }

    // ── Session management ────────────────────────────────────────────────────

    private async Task<string> EnsureSessionAsync(CancellationToken ct)
    {
        if (_sessionId is not null)
            return _sessionId;

        await _sessionLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_sessionId is not null)
                return _sessionId;

            var request = new StartCodeInterpreterSessionRequest
            {
                CodeInterpreterIdentifier = _codeInterpreterIdentifier,
                Name = $"strands-session-{Guid.NewGuid():N}"[..40],
            };

            var response = await _client.StartCodeInterpreterSessionAsync(request, ct)
                .ConfigureAwait(false);

            _sessionId = response.SessionId;
            return _sessionId;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_sessionId is not null)
        {
            try
            {
                await _client.StopCodeInterpreterSessionAsync(
                    new StopCodeInterpreterSessionRequest
                    {
                        CodeInterpreterIdentifier = _codeInterpreterIdentifier,
                        SessionId = _sessionId,
                    }).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup — don't throw from Dispose.
            }
        }

        _sessionLock.Dispose();

        if (_ownsClient)
            _client.Dispose();
    }
}
