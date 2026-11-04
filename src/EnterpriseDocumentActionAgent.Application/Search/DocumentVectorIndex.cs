using EnterpriseDocumentActionAgent.Application.Embeddings;

namespace EnterpriseDocumentActionAgent.Application.Search;

/// <summary>
/// Represents an indexed document passage and its embedding.
/// </summary>
/// <remarks>
/// TenantId and document access information must originate
/// from trusted ingestion infrastructure.
///
/// EmbeddingModel identifies the model used to generate
/// the vector. Vectors from incompatible models must
/// not be compared within the same search operation.
///
/// AllowedPrincipalIds and AllowedSecurityGroups define
/// the principals authorized to retrieve the passage.
///
/// An empty ACL does not grant public access.
/// </remarks>
public sealed record DocumentVectorChunk(
    Guid DocumentId,
    string TenantId,
    string ChunkId,
    string Text,
    IReadOnlyList<float> Vector,
    string EmbeddingModel,
    string DocumentVersion,
    DocumentSearchCitation Citation,
    IReadOnlyList<string> AllowedPrincipalIds,
    IReadOnlyList<string> AllowedSecurityGroups);

/// <summary>
/// Represents a request to upsert one or more vector chunks.
/// </summary>
/// <remarks>
/// Implementations must perform tenant-scoped writes.
///
/// A repeated upsert for the same document, version,
/// and chunk identifier should be idempotent.
/// </remarks>
public sealed record UpsertDocumentVectorsRequest(
    string TenantId,
    IReadOnlyList<DocumentVectorChunk> Chunks);

/// <summary>
/// Represents a request to atomically replace the
/// indexed chunks for one document.
/// </summary>
/// <remarks>
/// Implementations must prevent concurrent indexing
/// operations from publishing stale document versions.
///
/// Replacing a document's chunks must not temporarily
/// expose a mixture of old and new versions.
///
/// ExpectedIndexedVersion may be null when the
/// document has not previously been indexed.
/// </remarks>
public sealed record ReplaceDocumentVectorsRequest(
    Guid DocumentId,
    string TenantId,
    string NewDocumentVersion,
    string? ExpectedIndexedVersion,
    IReadOnlyList<DocumentVectorChunk> Chunks);

/// <summary>
/// Represents a request to remove indexed vectors
/// belonging to one document.
/// </summary>
public sealed record DeleteDocumentVectorsRequest(
    Guid DocumentId,
    string TenantId);

/// <summary>
/// Represents a vector similarity search request.
/// </summary>
/// <remarks>
/// SecurityContext must originate from authenticated
/// application infrastructure.
///
/// Implementations must apply tenant and ACL filters
/// before returning any matching document passages.
///
/// Score is normalized to the interval [0, 1].
/// </remarks>
public sealed record DocumentVectorSearchRequest(
    IReadOnlyList<float> QueryVector,
    string EmbeddingModel,
    DocumentSearchSecurityContext SecurityContext,
    int MaximumResults = 10);

/// <summary>
/// Represents one authorized vector search match.
/// </summary>
public sealed record DocumentVectorSearchHit(
    DocumentVectorChunk Chunk,
    double Score);

/// <summary>
/// Represents the result of a vector search.
/// </summary>
public sealed record DocumentVectorSearchResult(
    IReadOnlyList<DocumentVectorSearchHit> Hits);

/// <summary>
/// Defines the application-facing document vector index.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Enforce tenant isolation for reads and writes.
/// - Enforce document ACLs within vector queries.
/// - Prevent unauthorized text from being returned.
/// - Reject incompatible embedding models.
/// - Preserve citation metadata.
/// - Use atomic document-version replacement.
/// - Prevent stale indexing events from overwriting
///   newer indexed document versions.
/// - Make repeated indexing operations idempotent.
/// - Honor cancellation.
///
/// This interface does not authenticate callers
/// or authorize ingestion operations.
/// </remarks>
public interface IDocumentVectorIndex
{
    /// <summary>
    /// Upserts document vector chunks.
    /// </summary>
    Task UpsertAsync(
        UpsertDocumentVectorsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces all indexed chunks for one document.
    /// </summary>
    Task ReplaceDocumentAsync(
        ReplaceDocumentVectorsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes indexed chunks for one document.
    /// </summary>
    Task DeleteDocumentAsync(
        DeleteDocumentVectorsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches authorized document vectors.
    /// </summary>
    Task<DocumentVectorSearchResult> SearchAsync(
        DocumentVectorSearchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides provider-independent validation for
/// document vector indexing operations.
/// </summary>
/// <remarks>
/// Validation does not replace database-enforced
/// tenant isolation, ACL filtering, or concurrency
/// controls.
/// </remarks>
public static class DocumentVectorIndexValidation
{
    public const int MaximumChunksPerRequest = 1000;
    public const int MaximumChunkCharacters = 12000;
    public const int MaximumDocumentVersionCharacters = 256;
    public const int MaximumAclEntries = 256;

    /// <summary>
    /// Validates a vector upsert request.
    /// </summary>
    public static void ValidateUpsertRequest(
        UpsertDocumentVectorsRequest request,
        int expectedDimensions)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateIdentifier(
            request.TenantId,
            nameof(request.TenantId));

        ArgumentNullException.ThrowIfNull(request.Chunks);

        if (request.Chunks.Count == 0 ||
            request.Chunks.Count > MaximumChunksPerRequest)
        {
            throw new ArgumentException(
                "Vector upsert chunk count is invalid.",
                nameof(request));
        }

        var chunkKeys = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var chunk in request.Chunks)
        {
            ValidateChunk(
                chunk,
                expectedDimensions);

            if (!string.Equals(
                    chunk.TenantId,
                    request.TenantId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Vector upsert contains a chunk outside the tenant.",
                    nameof(request));
            }

            var chunkKey =
                $"{chunk.DocumentId:N}:{chunk.DocumentVersion}:{chunk.ChunkId}";

            if (!chunkKeys.Add(chunkKey))
            {
                throw new ArgumentException(
                    "Vector upsert contains duplicate chunk keys.",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Validates a document vector replacement request.
    /// </summary>
    public static void ValidateReplaceRequest(
        ReplaceDocumentVectorsRequest request,
        int expectedDimensions)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateDocumentId(request.DocumentId);

        ValidateIdentifier(
            request.TenantId,
            nameof(request.TenantId));

        ValidateDocumentVersion(
            request.NewDocumentVersion);

        if (request.ExpectedIndexedVersion is not null)
        {
            ValidateDocumentVersion(
                request.ExpectedIndexedVersion);
        }

        ArgumentNullException.ThrowIfNull(request.Chunks);

        if (request.Chunks.Count >
            MaximumChunksPerRequest)
        {
            throw new ArgumentException(
                "Vector replacement exceeds the chunk limit.",
                nameof(request));
        }

        var chunkIds = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var chunk in request.Chunks)
        {
            ValidateChunk(
                chunk,
                expectedDimensions);

            if (chunk.DocumentId != request.DocumentId ||
                !string.Equals(
                    chunk.TenantId,
                    request.TenantId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    chunk.DocumentVersion,
                    request.NewDocumentVersion,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Vector replacement contains a mismatched chunk.",
                    nameof(request));
            }

            if (!chunkIds.Add(chunk.ChunkId))
            {
                throw new ArgumentException(
                    "Vector replacement contains duplicate chunk IDs.",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Validates a document vector deletion request.
    /// </summary>
    public static void ValidateDeleteRequest(
        DeleteDocumentVectorsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateDocumentId(request.DocumentId);

        ValidateIdentifier(
            request.TenantId,
            nameof(request.TenantId));
    }

    /// <summary>
    /// Validates a vector similarity search request.
    /// </summary>
    public static void ValidateSearchRequest(
        DocumentVectorSearchRequest request,
        int expectedDimensions)
    {
        ArgumentNullException.ThrowIfNull(request);

        EmbeddingValidation.ValidateVector(
            request.QueryVector,
            expectedDimensions);

        ValidateIdentifier(
            request.EmbeddingModel,
            nameof(request.EmbeddingModel));

        DocumentSearchValidation.ValidateSecurityContext(
            request.SecurityContext);

        if (request.MaximumResults < 1 ||
            request.MaximumResults >
                DocumentSearchValidation.MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Vector search result limit is invalid.");
        }
    }

    /// <summary>
    /// Validates the structural integrity of a vector chunk.
    /// </summary>
    public static void ValidateChunk(
        DocumentVectorChunk chunk,
        int expectedDimensions)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        ValidateDocumentId(chunk.DocumentId);

        ValidateIdentifier(
            chunk.TenantId,
            nameof(chunk.TenantId));

        ValidateIdentifier(
            chunk.ChunkId,
            nameof(chunk.ChunkId));

        ValidateIdentifier(
            chunk.EmbeddingModel,
            nameof(chunk.EmbeddingModel));

        ValidateDocumentVersion(
            chunk.DocumentVersion);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            chunk.Text);

        if (chunk.Text.Length > MaximumChunkCharacters)
        {
            throw new ArgumentException(
                "Indexed text exceeds the maximum chunk length.",
                nameof(chunk));
        }

        if (chunk.Text.Contains('\0'))
        {
            throw new ArgumentException(
                "Indexed text contains an invalid null character.",
                nameof(chunk));
        }

        EmbeddingValidation.ValidateVector(
            chunk.Vector,
            expectedDimensions);

        ValidateCitation(chunk);

        ValidateAcl(
            chunk.AllowedPrincipalIds,
            nameof(chunk.AllowedPrincipalIds));

        ValidateAcl(
            chunk.AllowedSecurityGroups,
            nameof(chunk.AllowedSecurityGroups));
    }

    /// <summary>
    /// Validates vector search results.
    /// </summary>
    /// <remarks>
    /// These checks provide defense in depth.
    ///
    /// They cannot prove that database ACL filters
    /// were correctly applied.
    /// </remarks>
    public static void ValidateSearchResult(
        DocumentVectorSearchRequest request,
        DocumentVectorSearchResult result,
        int expectedDimensions)
    {
        ValidateSearchRequest(
            request,
            expectedDimensions);

        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(result.Hits);

        if (result.Hits.Count > request.MaximumResults)
        {
            throw new InvalidOperationException(
                "Vector search returned too many results.");
        }

        double? previousScore = null;

        foreach (var hit in result.Hits)
        {
            if (hit is null)
            {
                throw new InvalidOperationException(
                    "Vector search contains a null hit.");
            }

            ValidateChunk(
                hit.Chunk,
                expectedDimensions);

            if (!string.Equals(
                    hit.Chunk.TenantId,
                    request.SecurityContext.TenantId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Vector search returned a cross-tenant chunk.");
            }

            if (!string.Equals(
                    hit.Chunk.EmbeddingModel,
                    request.EmbeddingModel,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Vector search returned an incompatible embedding model.");
            }

            if (!double.IsFinite(hit.Score) ||
                hit.Score < 0d ||
                hit.Score > 1d)
            {
                throw new InvalidOperationException(
                    "Vector search returned an invalid relevance score.");
            }

            if (previousScore.HasValue &&
                hit.Score > previousScore.Value)
            {
                throw new InvalidOperationException(
                    "Vector search results are not ranked correctly.");
            }

            previousScore = hit.Score;
        }
    }

    private static void ValidateCitation(
        DocumentVectorChunk chunk)
    {
        if (chunk.Citation is null)
        {
            throw new ArgumentException(
                "Indexed chunk must include a citation.",
                nameof(chunk));
        }

        if (chunk.Citation.DocumentId != chunk.DocumentId ||
            !string.Equals(
                chunk.Citation.ChunkId,
                chunk.ChunkId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Indexed chunk citation does not match its identity.",
                nameof(chunk));
        }

        if (chunk.Citation.PageNumber is <= 0)
        {
            throw new ArgumentException(
                "Indexed chunk citation page number must be positive.",
                nameof(chunk));
        }
    }

    private static void ValidateAcl(
        IReadOnlyList<string> entries,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count > MaximumAclEntries)
        {
            throw new ArgumentException(
                "Indexed chunk ACL exceeds the permitted size.",
                parameterName);
        }

        var uniqueEntries = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            ValidateIdentifier(
                entry,
                parameterName);

            if (!uniqueEntries.Add(entry))
            {
                throw new ArgumentException(
                    "Indexed chunk ACL contains duplicate entries.",
                    parameterName);
            }
        }
    }

    private static void ValidateDocumentVersion(
        string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            version);

        if (version.Length >
            MaximumDocumentVersionCharacters)
        {
            throw new ArgumentException(
                "Document version exceeds the permitted length.",
                nameof(version));
        }

        if (version != version.Trim() ||
            version.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Document version must be normalized.",
                nameof(version));
        }
    }

    private static void ValidateDocumentId(
        Guid documentId)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(documentId));
        }
    }

    private static void ValidateIdentifier(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);

        if (value.Length >
            DocumentSearchValidation.MaximumIdentifierCharacters)
        {
            throw new ArgumentException(
                "Identifier exceeds the permitted length.",
                parameterName);
        }

        if (value != value.Trim() ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Identifier must be normalized.",
                parameterName);
        }
    }
}