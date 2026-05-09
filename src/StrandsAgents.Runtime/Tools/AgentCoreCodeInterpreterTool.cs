using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using StrandsAgents.Core;

namespace StrandsAgents.Runtime.Tools;

/// <summary>
/// Managed code execution sandbox via Amazon Bedrock AgentCore Code Interpreter.
///
/// <para>
/// Executes code in an isolated, managed sandbox. Supports multiple languages.
/// The sandbox is stateless between calls — variables and state do not persist
/// across invocations.
/// </para>
/// </summary>
public sealed class AgentCoreCodeInterpreterTool : ITool, IAsyncDisposable
{
    private static readonly ToolDefinition _definition = new(
        Name: "agentcore_code_interpreter",
        Description: """
            Executes code in a managed sandbox provided by Amazon Bedrock AgentCore.
            Returns stdout, stderr, and exit code.
            The sandbox is isolated and stateless between calls.
            Supported languages: python, javascript, bash.
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
                  "enum": ["python", "javascript", "bash"],
                  "description": "Programming language of the code."
                }
              },
              "required": ["code", "language"]
            }
            """).RootElement.Clone());

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    /// <summary>
    /// Initialises a new <see cref="AgentCoreCodeInterpreterTool"/>.
    /// </summary>
    /// <param name="region">AWS region. Default: <c>us-east-1</c>.</param>
    /// <param name="clientOverride">
    /// Optional pre-configured <see cref="HttpClient"/>. When provided, the tool does
    /// not own the client and will not dispose it. Intended for testing.
    /// </param>
    public AgentCoreCodeInterpreterTool(
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
        if (!input.TryGetProperty("code", out var codeEl) ||
            !input.TryGetProperty("language", out var langEl))
            return ToolResult.Failure("agentcore_code_interpreter",
                "Missing required fields: code, language.");

        var code = codeEl.GetString();
        var language = langEl.GetString();

        if (string.IsNullOrWhiteSpace(code))
            return ToolResult.Failure("agentcore_code_interpreter", "code must be a non-empty string.");

        if (string.IsNullOrWhiteSpace(language))
            return ToolResult.Failure("agentcore_code_interpreter", "language must be a non-empty string.");

        var payload = new { code, language };

        using var response = await _http
            .PostAsJsonAsync("/code-interpreter/execute", payload, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return ToolResult.Failure("agentcore_code_interpreter",
                $"Code execution failed. Status: {(int)response.StatusCode}");

        var result = await response.Content
            .ReadFromJsonAsync<CodeExecutionResult>(ct)
            .ConfigureAwait(false);

        if (result is null)
            return ToolResult.Failure("agentcore_code_interpreter", "Empty response from code interpreter.");

        var summary = $"Exit code: {result.ExitCode}\n\nStdout:\n{result.Stdout}\n\nStderr:\n{result.Stderr}";
        var isError = result.ExitCode != 0;

        return isError
            ? ToolResult.Failure("agentcore_code_interpreter", summary)
            : ToolResult.Success("agentcore_code_interpreter", summary);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_ownsClient)
            _http.Dispose();

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    private sealed record CodeExecutionResult(
        [property: JsonPropertyName("stdout")] string Stdout,
        [property: JsonPropertyName("stderr")] string Stderr,
        [property: JsonPropertyName("exitCode")] int ExitCode);
}
