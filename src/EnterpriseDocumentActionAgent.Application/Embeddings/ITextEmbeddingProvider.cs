namespace EnterpriseDocumentActionAgent.Application.Embeddings;

/// <summary>
/// Represents a request to generate an embedding
/// for a single text passage.
/// </summary>
/// <remarks>
/// RequestId is a correlation identifier for
/// tracing and diagnostics.
///
/// It must not be used as an authorization token.
///
/// Text may contain sensitive document content.
/// Callers and implementations must avoid logging
/// raw text unless explicitly permitted.
/// </remarks>
public sealed record TextEmbeddingRequest(
    string RequestId,
    string Text);

/// <summary>
/// Represents a generated text embedding.
/// </summary>
/// <remarks>
/// Vector contains the numeric embedding values.
///
/// The vector dimension must match the configured
/// embedding model and vector index.
///
/// This record does not validate vector values.
/// Validation is implemented separately.
/// </remarks>
public sealed record TextEmbeddingResult(
    string RequestId,
    IReadOnlyList<float> Vector,
    string Model,
    int Dimensions);

/// <summary>
/// Represents a batch of text passages to embed.
/// </summary>
public sealed record TextEmbeddingBatchRequest(
    IReadOnlyList<TextEmbeddingRequest> Items);

/// <summary>
/// Represents the result of a batch embedding request.
/// </summary>
/// <remarks>
/// Implementations must return one result for each
/// requested item.
///
/// Results must preserve input order so callers
/// can associate embeddings with their source text.
///
/// The application must not assume that different
/// embedding models produce compatible vectors.
/// </remarks>
public sealed record TextEmbeddingBatchResult(
    IReadOnlyList<TextEmbeddingResult> Items);

/// <summary>
/// Describes the configured embedding provider.
/// </summary>
/// <remarks>
/// Dimensions is the number of numeric values
/// produced by the embedding model.
///
/// MaximumBatchSize is the maximum number of text
/// passages accepted in a single batch request.
///
/// MaximumInputCharacters is an application-side
/// safety limit. It is not a substitute for
/// model-specific token validation.
/// </remarks>
public sealed record TextEmbeddingProviderDescriptor(
    string Provider,
    string Model,
    int Dimensions,
    int MaximumBatchSize,
    int MaximumInputCharacters);

/// <summary>
/// Defines a provider-independent interface for
/// generating semantic text embeddings.
/// </summary>
/// <remarks>
/// Implementations may use Azure OpenAI or another
/// embedding service.
///
/// The interface intentionally does not expose
/// provider-specific SDK request or response types.
///
/// Implementations must:
///
/// - Reject invalid or empty input text.
/// - Enforce configured batch-size limits.
/// - Enforce provider-specific token limits.
/// - Return finite numeric vector values.
/// - Return vectors with the configured dimensions.
/// - Preserve batch input order.
/// - Honor cancellation requests.
/// - Avoid logging sensitive document content.
/// - Avoid returning partial batches as successful.
///
/// Retries must respect provider rate limits and
/// cancellation.
///
/// This interface does not authenticate users,
/// enforce tenant isolation, or authorize access
/// to source documents. Those controls belong
/// to the calling application and data-access layers.
/// </remarks>
public interface ITextEmbeddingProvider
{
    /// <summary>
    /// Gets the configured embedding provider details.
    /// </summary>
    TextEmbeddingProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Generates an embedding for a single text passage.
    /// </summary>
    Task<TextEmbeddingResult> GenerateAsync(
        TextEmbeddingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embeddings for a batch of text passages.
    /// </summary>
    /// <remarks>
    /// The returned results must preserve the same
    /// order as the input requests.
    ///
    /// Implementations must either return a complete
    /// valid batch or throw an exception.
    /// </remarks>
    Task<TextEmbeddingBatchResult> GenerateBatchAsync(
        TextEmbeddingBatchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a failure while generating embeddings.
/// </summary>
/// <remarks>
/// This exception deliberately avoids storing the
/// original text to reduce accidental disclosure
/// through logs and exception telemetry.
///
/// IsTransient indicates whether retrying may be
/// appropriate.
///
/// A transient failure does not guarantee that
/// retrying immediately will succeed.
/// </remarks>
public sealed class TextEmbeddingProviderException : Exception
{
    /// <summary>
    /// Gets the configured provider identifier.
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// Gets whether the failure may be transient.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>
    /// Gets the recommended retry delay, if available.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Creates an embedding provider exception.
    /// </summary>
    public TextEmbeddingProviderException(
        string provider,
        string message,
        bool isTransient = false,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            provider);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            message);

        if (provider.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Provider identifier cannot exceed 128 characters.",
                nameof(provider));
        }

        if (retryAfter.HasValue &&
            retryAfter.Value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryAfter),
                "Retry delay cannot be negative.");
        }

        Provider = provider.Trim();
        IsTransient = isTransient;
        RetryAfter = retryAfter;
    }
}