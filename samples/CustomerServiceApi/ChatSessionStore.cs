using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Jacquard.Core;

namespace CustomerServiceApi;

/// <summary>
/// Singleton that owns per-session <see cref="Agent"/> instances.
/// Each session gets its own agent so conversation history is isolated.
/// </summary>
public sealed class ChatSessionStore
{
    private readonly IModel _model;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<ChatSessionStore> _logger;
    private readonly ITool[] _tools;
    private readonly ConcurrentDictionary<string, Agent> _sessions = new(StringComparer.Ordinal);

    private const string SystemPrompt =
        "You are a helpful customer service agent for an e-commerce store. " +
        "Always be polite, concise, and empathetic. " +
        "Use the available tools to look up order status and search the knowledge base " +
        "before answering questions — do not guess at policies or order details. " +
        "If you cannot resolve the issue, politely offer to escalate to a human agent.";

    /// <summary>
    /// Initialises the store with the model and session manager resolved from DI.
    /// </summary>
    public ChatSessionStore(
        IModel model,
        ISessionManager sessionManager,
        ILogger<ChatSessionStore> logger,
        IEnumerable<ITool> tools)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tools = tools.ToArray();
    }

    /// <summary>
    /// Returns the existing session agent, or creates a new one if the session is unknown.
    /// </summary>
    public Agent GetOrCreate(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return _sessions.GetOrAdd(sessionId, id =>
        {
            _logger.LogInformation("Creating new session {SessionId}", id);

            var hooks = new HookRegistry();

            hooks.Register<BeforeToolCallEvent>(e =>
            {
                _logger.LogDebug("[{SessionId}] Tool call: {Tool}({Input})", id, e.Call.Name, e.Call.Input);
                return Task.CompletedTask;
            });

            hooks.Register<AfterToolCallEvent>(e =>
            {
                _logger.LogDebug("[{SessionId}] Tool result: {Tool} → {Result}",
                    id, e.Call.Name, e.Result.Content);
                return Task.CompletedTask;
            });

            hooks.Register<AfterModelCallEvent>(e =>
            {
                _logger.LogDebug("[{SessionId}] Tokens: in={In} out={Out}",
                    id, e.Response.Usage.InputTokens, e.Response.Usage.OutputTokens);
                return Task.CompletedTask;
            });

            return new Agent(
                _model,
                systemPrompt: SystemPrompt,
                tools: _tools,
                sessionManager: _sessionManager,
                hooks: hooks);
        });
    }

    /// <summary>Returns <see langword="true"/> when the session exists and removes it.</summary>
    public bool Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);

    /// <summary>Returns the IDs of all active sessions.</summary>
    public IReadOnlyCollection<string> SessionIds => _sessions.Keys.ToArray();
}
