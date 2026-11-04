namespace EnterpriseDocumentActionAgent.Application.Embeddings;

/// <summary>
/// Provides centralized validation for text embedding
/// requests, provider configurations, and generated results.
/// </summary>
/// <remarks>
/// This class contains provider-independent validation rules.
///
/// Provider implementations must additionally enforce
/// model-specific token limits and service constraints.
///
/// Validation does not authenticate callers, authorize
/// document access, or enforce tenant isolation.
///
/// Text and vector values are intentionally excluded
/// from exception messages to reduce accidental
/// disclosure of sensitive information.
/// </remarks>
public static class EmbeddingValidation
{
    /// <summary>
    /// Maximum permitted length of an embedding
    /// request correlation identifier.
    /// </summary>
    public const int MaximumRequestIdLength = 128;

    /// <summary>
    /// Maximum permitted length of provider
    /// and model identifiers.
    /// </summary>
    public const int MaximumIdentifierLength = 256;

    /// <summary>
    /// Validates an embedding provider descriptor.
    /// </summary>
    public static void ValidateDescriptor(
        TextEmbeddingProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        ValidateIdentifier(
            descriptor.Provider,
            nameof(descriptor.Provider));

        ValidateIdentifier(
            descriptor.Model,
            nameof(descriptor.Model));

        if (descriptor.Dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                "Embedding dimensions must be positive.");
        }

        if (descriptor.MaximumBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                "Maximum batch size must be positive.");
        }

        if (descriptor.MaximumInputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(descriptor),
                "Maximum input characters must be positive.");
        }
    }

    /// <summary>
    /// Validates a single embedding request against
    /// the configured provider limits.
    /// </summary>
    public static void ValidateRequest(
        TextEmbeddingRequest request,
        TextEmbeddingProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateDescriptor(descriptor);

        ValidateRequestId(request.RequestId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Text);

        if (request.Text.Length >
            descriptor.MaximumInputCharacters)
        {
            throw new ArgumentException(
                "Embedding input exceeds the configured " +
                "maximum character count.",
                nameof(request));
        }

        if (request.Text.Any(
                character =>
                    character == '\0'))
        {
            throw new ArgumentException(
                "Embedding input contains an invalid " +
                "null character.",
                nameof(request));
        }
    }

    /// <summary>
    /// Validates a batch embedding request.
    /// </summary>
    /// <remarks>
    /// A batch must contain at least one item and
    /// must not exceed the provider's batch limit.
    ///
    /// Request identifiers must be unique within
    /// each batch.
    /// </remarks>
    public static void ValidateBatchRequest(
        TextEmbeddingBatchRequest request,
        TextEmbeddingProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateDescriptor(descriptor);

        ArgumentNullException.ThrowIfNull(
            request.Items);

        if (request.Items.Count == 0)
        {
            throw new ArgumentException(
                "Embedding batch cannot be empty.",
                nameof(request));
        }

        if (request.Items.Count >
            descriptor.MaximumBatchSize)
        {
            throw new ArgumentException(
                "Embedding batch exceeds the configured " +
                "maximum batch size.",
                nameof(request));
        }

        var requestIds = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var item in request.Items)
        {
            if (item is null)
            {
                throw new ArgumentException(
                    "Embedding batch contains a null item.",
                    nameof(request));
            }

            ValidateRequest(item, descriptor);

            if (!requestIds.Add(item.RequestId))
            {
                throw new ArgumentException(
                    "Embedding batch contains duplicate " +
                    "request identifiers.",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Validates a generated embedding result.
    /// </summary>
    /// <remarks>
    /// The result must match the configured model
    /// and embedding dimensions.
    ///
    /// Every vector element must be finite.
    /// </remarks>
    public static void ValidateResult(
        TextEmbeddingResult result,
        TextEmbeddingProviderDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(result);

        ValidateDescriptor(descriptor);

        ValidateRequestId(result.RequestId);

        ValidateIdentifier(
            result.Model,
            nameof(result.Model));

        if (!string.Equals(
                result.Model,
                descriptor.Model,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Embedding result model does not match " +
                "the configured embedding model.");
        }

        if (result.Dimensions != descriptor.Dimensions)
        {
            throw new InvalidOperationException(
                "Embedding result dimensions do not match " +
                "the configured embedding dimensions.");
        }

        ValidateVector(
            result.Vector,
            descriptor.Dimensions);
    }

    /// <summary>
    /// Validates an embedding vector.
    /// </summary>
    /// <remarks>
    /// Vectors must have exactly the expected number
    /// of dimensions and contain only finite values.
    ///
    /// Zero vectors are rejected because they are
    /// unsuitable for cosine-similarity retrieval.
    /// </remarks>
    public static void ValidateVector(
        IReadOnlyList<float> vector,
        int expectedDimensions)
    {
        ArgumentNullException.ThrowIfNull(vector);

        if (expectedDimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedDimensions),
                "Expected dimensions must be positive.");
        }

        if (vector.Count != expectedDimensions)
        {
            throw new InvalidOperationException(
                "Embedding vector dimensions do not match " +
                "the expected dimensions.");
        }

        var hasNonZeroValue = false;

        for (var index = 0; index < vector.Count; index++)
        {
            var value = vector[index];

            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException(
                    "Embedding vector contains a " +
                    "non-finite numeric value.");
            }

            if (value != 0f)
            {
                hasNonZeroValue = true;
            }
        }

        if (!hasNonZeroValue)
        {
            throw new InvalidOperationException(
                "Embedding vector cannot contain " +
                "only zero values.");
        }
    }

    /// <summary>
    /// Validates a batch embedding response against
    /// the corresponding batch request.
    /// </summary>
    /// <remarks>
    /// The response must contain exactly one result
    /// for every requested item.
    ///
    /// Results must preserve input order and
    /// correlation identifiers.
    /// </remarks>
    public static void ValidateBatchResult(
        TextEmbeddingBatchRequest request,
        TextEmbeddingBatchResult result,
        TextEmbeddingProviderDescriptor descriptor)
    {
        ValidateBatchRequest(request, descriptor);

        ArgumentNullException.ThrowIfNull(result);

        ArgumentNullException.ThrowIfNull(
            result.Items);

        if (result.Items.Count != request.Items.Count)
        {
            throw new InvalidOperationException(
                "Embedding batch result count does not " +
                "match the request count.");
        }

        for (var index = 0;
             index < request.Items.Count;
             index++)
        {
            var expectedRequest = request.Items[index];
            var actualResult = result.Items[index];

            if (actualResult is null)
            {
                throw new InvalidOperationException(
                    "Embedding batch contains a null result.");
            }

            ValidateResult(
                actualResult,
                descriptor);

            if (!string.Equals(
                    actualResult.RequestId,
                    expectedRequest.RequestId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Embedding batch result order or " +
                    "request identifiers do not match.");
            }
        }
    }

    /// <summary>
    /// Validates a request correlation identifier.
    /// </summary>
    public static void ValidateRequestId(
        string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            requestId);

        if (requestId.Length > MaximumRequestIdLength)
        {
            throw new ArgumentException(
                "Embedding request identifier exceeds " +
                "the maximum permitted length.",
                nameof(requestId));
        }

        if (!string.Equals(
                requestId,
                requestId.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Embedding request identifier must " +
                "be normalized.",
                nameof(requestId));
        }

        if (requestId.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Embedding request identifier cannot " +
                "contain control characters.",
                nameof(requestId));
        }
    }

    private static void ValidateIdentifier(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);

        if (value.Length > MaximumIdentifierLength)
        {
            throw new ArgumentException(
                "Embedding identifier exceeds the " +
                "maximum permitted length.",
                parameterName);
        }

        if (!string.Equals(
                value,
                value.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Embedding identifier must be normalized.",
                parameterName);
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Embedding identifier cannot contain " +
                "control characters.",
                parameterName);
        }
    }
}