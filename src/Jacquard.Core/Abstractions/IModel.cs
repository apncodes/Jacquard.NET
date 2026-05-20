namespace Jacquard.Core;

/// <summary>Abstraction over any LLM provider.</summary>
public interface IModel
{
    Task<ModelResponse> InvokeAsync(ModelRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ModelStreamEvent> StreamAsync(ModelRequest request, CancellationToken ct = default);
}
