namespace Jacquard.Core;

/// <summary>
/// Thrown when <see cref="Agent.GetStructuredOutputAsync{T}"/> cannot deserialize
/// the model response into the requested type — either because the JSON is malformed
/// or because required fields are missing.
/// </summary>
public sealed class StructuredOutputException : StrandsException
{
    /// <summary>The raw text returned by the model before deserialization was attempted.</summary>
    public string RawResponse { get; }

    /// <summary>
    /// Initializes a new <see cref="StructuredOutputException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="rawResponse">The raw model response that could not be deserialized.</param>
    /// <param name="inner">The underlying deserialization exception, if any.</param>
    public StructuredOutputException(string message, string rawResponse, Exception? inner = null)
        : base(message, conversationSnapshot: null, inner)
    {
        RawResponse = rawResponse;
    }
}
