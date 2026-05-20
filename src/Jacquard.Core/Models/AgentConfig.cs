namespace Jacquard.Core;

/// <summary>Configuration for agent behaviour.</summary>
public record AgentConfig
{
    /// <summary>Maximum event loop iterations before stopping. Default: 10.</summary>
    public int MaxIterations { get; init; } = 10;

    /// <summary>Execute multiple tool calls in parallel when the model requests them. Default: true.</summary>
    public bool ParallelToolExecution { get; init; } = true;

    /// <summary>
    /// Strategy used to trim the conversation history to fit within the context window
    /// before each model call. When <c>null</c> (the default), no trimming is applied.
    /// </summary>
    public IContextWindowStrategy? ContextWindowStrategy { get; init; }

    /// <summary>
    /// Maximum token budget passed to <see cref="ContextWindowStrategy"/> when trimming.
    /// Only used when <see cref="ContextWindowStrategy"/> is non-null. Default: 100,000.
    /// </summary>
    public int MaxContextTokens { get; init; } = 100_000;
}
