using System.Net.Http.Json;
using StrandsAgents.Core;

namespace StrandsAgents.MultiAgent;

/// <summary>
/// An <see cref="IAgent"/> that forwards invocations to a remote A2A endpoint.
/// </summary>
public sealed class A2AAgent : IAgent, IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly bool _ownsClient;

    public A2AAgent(Uri endpoint, HttpClient? httpClient = null)
    {
        _endpoint = endpoint;
        if (httpClient is not null)
        {
            _http = httpClient;
            _ownsClient = false;
        }
        else
        {
            _http = new HttpClient();
            _ownsClient = true;
        }
    }

    /// <inheritdoc/>
    public async Task<AgentResult> InvokeAsync(string prompt, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(_endpoint, new A2ARequest(prompt), ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var a2aResponse = await response.Content.ReadFromJsonAsync<A2AResponse>(ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("A2A server returned null response.");

        var stopReason = Enum.TryParse<StopReason>(a2aResponse.StopReason, out var sr)
            ? sr : StopReason.EndTurn;
        var usage = new TokenUsage(a2aResponse.InputTokens, a2aResponse.OutputTokens);
        var metrics = new AgentMetrics(TimeSpan.Zero, 1, 0, usage);
        return new AgentResult(a2aResponse.Message, stopReason, usage, metrics);
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">A2AAgent does not support streaming.</exception>
    public IAsyncEnumerable<StreamEvent> StreamAsync(string prompt, CancellationToken ct = default)
        => throw new NotSupportedException("A2AAgent does not support streaming. Use InvokeAsync.");

    /// <inheritdoc/>
    public void Dispose()
    {
        // Only dispose the HttpClient when we created it; externally-provided clients are
        // the caller's responsibility to dispose.
        if (_ownsClient)
            _http.Dispose();
    }
}
