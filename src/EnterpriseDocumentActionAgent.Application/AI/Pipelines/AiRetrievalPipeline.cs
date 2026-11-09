namespace EnterpriseDocumentActionAgent.Application.AI.Pipelines;

/// <summary>
/// Represents a document passage returned by
/// an enterprise retrieval provider.
/// </summary>
/// <remarks>
/// RelevanceScore must be a finite value
/// between zero and one.
///
/// SourceReference may contain a document URI,
/// section identifier, or another reference
/// suitable for displaying source attribution.
///
/// The presence of a source reference does
/// not establish that the source is trustworthy
/// or that the caller is authorized to access it.
/// </remarks>
public sealed record AiRetrievedPassage
{
    /// <summary>
    /// Gets the identifier of the source document.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    /// Gets the identifier of the retrieved passage.
    /// </summary>
    public string PassageId { get; }

    /// <summary>
    /// Gets the retrieved passage text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the normalized relevance score.
    /// </summary>
    public double RelevanceScore { get; }

    /// <summary>
    /// Gets an optional source reference.
    /// </summary>
    public string? SourceReference { get; }

    /// <summary>
    /// Initializes a retrieved document passage.
    /// </summary>
    public AiRetrievedPassage(
        string documentId,
        string passageId,
        string text,
        double relevanceScore,
        string? sourceReference = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            documentId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            passageId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            text);

        if (!double.IsFinite(relevanceScore) ||
            relevanceScore < 0 ||
            relevanceScore > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relevanceScore),
                "Relevance score must be between zero and one.");
        }

        DocumentId = documentId.Trim();

        PassageId = passageId.Trim();

        Text = text;

        RelevanceScore = relevanceScore;

        SourceReference = string.IsNullOrWhiteSpace(
            sourceReference)
            ? null
            : sourceReference.Trim();
    }
}

/// <summary>
/// Defines the provider-neutral contract
/// for retrieving relevant document passages.
/// </summary>
/// <remarks>
/// Implementations may use keyword search,
/// vector similarity, hybrid search, or
/// another retrieval strategy.
///
/// Providers must enforce applicable document
/// access permissions before returning results.
///
/// Implementations should honor cancellation
/// and document their relevance-score semantics.
/// </remarks>
public interface IAiRetrievalProvider
{
    /// <summary>
    /// Retrieves passages relevant to a query.
    /// </summary>
    Task<IReadOnlyList<AiRetrievedPassage>> RetrieveAsync(
        string query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines filtering and ranking options
/// for an AI retrieval pipeline.
/// </summary>
public sealed record AiRetrievalOptions
{
    /// <summary>
    /// Gets the maximum number of returned passages.
    /// </summary>
    public int MaxResults { get; }

    /// <summary>
    /// Gets the minimum accepted relevance score.
    /// </summary>
    public double MinimumRelevanceScore { get; }

    /// <summary>
    /// Initializes retrieval options.
    /// </summary>
    public AiRetrievalOptions(
        int maxResults = 5,
        double minimumRelevanceScore = 0)
    {
        if (maxResults <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                "Maximum result count must be positive.");
        }

        if (!double.IsFinite(minimumRelevanceScore) ||
            minimumRelevanceScore < 0 ||
            minimumRelevanceScore > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumRelevanceScore),
                "Minimum relevance score must be between zero and one.");
        }

        MaxResults = maxResults;

        MinimumRelevanceScore = minimumRelevanceScore;
    }
}

/// <summary>
/// Retrieves, filters, ranks, and limits
/// document passages for AI pipelines.
/// </summary>
/// <remarks>
/// This component performs post-retrieval
/// processing only.
///
/// It does not generate embeddings, query
/// a database directly, or enforce document
/// authorization independently of its provider.
///
/// Results are ordered by descending relevance.
/// Equal scores preserve the original provider
/// result order.
///
/// This pipeline does not perform semantic
/// reranking or deduplication.
/// </remarks>
public sealed class AiRetrievalPipeline
    : IAiRunnable<
        string,
        IReadOnlyList<AiRetrievedPassage>>
{
    private readonly IAiRetrievalProvider _provider;

    private readonly AiRetrievalOptions _options;

    /// <summary>
    /// Gets the configured retrieval options.
    /// </summary>
    public AiRetrievalOptions Options => _options;

    /// <summary>
    /// Initializes the retrieval pipeline.
    /// </summary>
    public AiRetrievalPipeline(
        IAiRetrievalProvider provider,
        AiRetrievalOptions? options = null)
    {
        _provider = provider
            ?? throw new ArgumentNullException(
                nameof(provider));

        _options = options
            ?? new AiRetrievalOptions();
    }

    /// <summary>
    /// Retrieves and ranks relevant passages.
    /// </summary>
    public async Task<IReadOnlyList<AiRetrievedPassage>>
        RunAsync(
            string input,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        cancellationToken.ThrowIfCancellationRequested();

        var query = input.Trim();

        var retrieved = await _provider.RetrieveAsync(
            query,
            cancellationToken).ConfigureAwait(false);

        if (retrieved is null)
        {
            throw new InvalidOperationException(
                "Retrieval provider returned a null result collection.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (retrieved.Any(static passage => passage is null))
        {
            throw new InvalidOperationException(
                "Retrieval provider returned a null passage.");
        }

        var ranked = retrieved
            .Where(passage =>
                passage.RelevanceScore >=
                _options.MinimumRelevanceScore)
            .OrderByDescending(
                static passage => passage.RelevanceScore)
            .Take(_options.MaxResults)
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();

        return Array.AsReadOnly(ranked);
    }

    /// <summary>
    /// Executes retrieval using the pipeline's
    /// configured provider and options.
    /// </summary>
    public Task<IReadOnlyList<AiRetrievedPassage>>
        RetrieveAsync(
            string query,
            CancellationToken cancellationToken = default)
    {
        return RunAsync(
            query,
            cancellationToken);
    }
}

/// <summary>
/// Adapts an asynchronous retrieval delegate
/// to the retrieval provider contract.
/// </summary>
public sealed class AiDelegateRetrievalProvider
    : IAiRetrievalProvider
{
    private readonly Func<
        string,
        CancellationToken,
        Task<IReadOnlyList<AiRetrievedPassage>>> _handler;

    /// <summary>
    /// Initializes a delegate-backed
    /// retrieval provider.
    /// </summary>
    public AiDelegateRetrievalProvider(
        Func<
            string,
            CancellationToken,
            Task<IReadOnlyList<AiRetrievedPassage>>> handler)
    {
        _handler = handler
            ?? throw new ArgumentNullException(
                nameof(handler));
    }

    /// <summary>
    /// Invokes the configured retrieval delegate.
    /// </summary>
    public Task<IReadOnlyList<AiRetrievedPassage>>
        RetrieveAsync(
            string query,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        cancellationToken.ThrowIfCancellationRequested();

        var task = _handler(
            query,
            cancellationToken);

        return task
            ?? throw new InvalidOperationException(
                "Retrieval handler returned a null task.");
    }
}