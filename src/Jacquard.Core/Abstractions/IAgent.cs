using System.Runtime.CompilerServices;

namespace Jacquard.Core;

/// <summary>Agent entry point — invoke or stream responses.</summary>
public interface IAgent
{
    Task<AgentResult> InvokeAsync(string prompt, CancellationToken ct = default);
    IAsyncEnumerable<StreamEvent> StreamAsync(string prompt, CancellationToken ct = default);
}
