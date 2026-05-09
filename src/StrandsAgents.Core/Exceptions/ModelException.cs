namespace StrandsAgents.Core;

/// <summary>
/// Thrown when a model provider returns a non-success response or encounters a protocol error.
/// Carries the originating request and optional HTTP status code for diagnostics.
/// </summary>
public sealed class ModelException : StrandsException
{
    /// <summary>The model request that triggered the failure.</summary>
    public ModelRequest Request { get; }

    /// <summary>
    /// The HTTP status code returned by the provider, or <c>null</c> for non-HTTP errors
    /// (e.g., network timeouts, SDK-level exceptions).
    /// </summary>
    public int? HttpStatusCode { get; }

    /// <summary>
    /// Initializes a new <see cref="ModelException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="request">The model request that caused the failure.</param>
    /// <param name="httpStatusCode">The HTTP status code, if applicable.</param>
    /// <param name="conversationSnapshot">The conversation history at the time of failure, if available.</param>
    /// <param name="inner">The underlying exception, if any.</param>
    public ModelException(
        string message,
        ModelRequest request,
        int? httpStatusCode = null,
        IReadOnlyList<Message>? conversationSnapshot = null,
        Exception? inner = null)
        : base(message, conversationSnapshot, inner)
    {
        Request = request;
        HttpStatusCode = httpStatusCode;
    }
}
