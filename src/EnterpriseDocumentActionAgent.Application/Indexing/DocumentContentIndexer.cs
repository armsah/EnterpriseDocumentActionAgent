using System.Globalization;
using EnterpriseDocumentActionAgent.Application.Embeddings;
using EnterpriseDocumentActionAgent.Application.Search;

namespace EnterpriseDocumentActionAgent.Application.Indexing;

/// <summary>
/// Represents trusted document content submitted for indexing.
/// </summary>
/// <remarks>
/// TenantId, document identity, version, and ACLs must
/// originate from trusted ingestion infrastructure.
///
/// Content is expected to contain extracted plain text.
///
/// ExpectedIndexedVersion is an optimistic-concurrency
/// precondition for replacing existing indexed chunks.
/// </remarks>
public sealed record IndexDocumentContentRequest(
    Guid DocumentId,
    string TenantId,
    string DocumentVersion,
    string Content,
    string? DocumentTitle,
    IReadOnlyList<string> AllowedPrincipalIds,
    IReadOnlyList<string> AllowedSecurityGroups,
    string? ExpectedIndexedVersion = null);

/// <summary>
/// Configures deterministic text chunking.
/// </summary>
public sealed record DocumentContentIndexingOptions(
    int ChunkSizeCharacters = 2000,
    int ChunkOverlapCharacters = 200,
    int MaximumDocumentCharacters = 2_000_000);

/// <summary>
/// Summarizes a completed document indexing operation.
/// </summary>
public sealed record IndexDocumentContentResult(
    Guid DocumentId,
    string TenantId,
    string DocumentVersion,
    int ChunkCount,
    string EmbeddingModel,
    int EmbeddingDimensions);

/// <summary>
/// Defines the application-facing document content indexer.
/// </summary>
public interface IDocumentContentIndexer
{
    /// <summary>
    /// Extracts chunks, generates embeddings, and replaces
    /// the document's indexed vectors.
    /// </summary>
    Task<IndexDocumentContentResult> IndexAsync(
        IndexDocumentContentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Converts extracted document text into indexed vector chunks.
/// </summary>
/// <remarks>
/// The indexer:
///
/// - Validates document identity and tenant scope.
/// - Uses deterministic character-based chunking.
/// - Generates embeddings in bounded batches.
/// - Validates provider responses.
/// - Preserves document and chunk identity.
/// - Copies trusted ACLs into each indexed chunk.
/// - Replaces indexed document chunks atomically
///   through IDocumentVectorIndex.
///
/// The vector-index implementation is responsible
/// for enforcing storage-level tenant isolation,
/// atomic replacement, and optimistic concurrency.
///
/// This component does not perform OCR, parse PDFs,
/// authenticate callers, or authorize documents.
///
/// Text is not included in exception messages.
/// </remarks>
public sealed class DocumentContentIndexer
    : IDocumentContentIndexer
{
    private readonly ITextEmbeddingProvider _embeddingProvider;
    private readonly IDocumentVectorIndex _vectorIndex;
    private readonly DocumentContentIndexingOptions _options;

    public DocumentContentIndexer(
        ITextEmbeddingProvider embeddingProvider,
        IDocumentVectorIndex vectorIndex,
        DocumentContentIndexingOptions? options = null)
    {
        _embeddingProvider = embeddingProvider
            ?? throw new ArgumentNullException(
                nameof(embeddingProvider));

        _vectorIndex = vectorIndex
            ?? throw new ArgumentNullException(
                nameof(vectorIndex));

        _options = options
            ?? new DocumentContentIndexingOptions();

        ValidateOptions(_options);

        EmbeddingValidation.ValidateDescriptor(
            _embeddingProvider.Descriptor);

        if (_options.ChunkSizeCharacters >
            _embeddingProvider.Descriptor.MaximumInputCharacters)
        {
            throw new ArgumentException(
                "Chunk size exceeds the embedding provider input limit.",
                nameof(options));
        }
    }

    /// <inheritdoc />
    public async Task<IndexDocumentContentResult> IndexAsync(
        IndexDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = _embeddingProvider.Descriptor;

        EmbeddingValidation.ValidateDescriptor(descriptor);

        var textChunks = SplitIntoChunks(
            request.Content,
            _options.ChunkSizeCharacters,
            _options.ChunkOverlapCharacters);

        if (textChunks.Count >
            DocumentVectorIndexValidation.MaximumChunksPerRequest)
        {
            throw new InvalidOperationException(
                "Document produces more chunks than the vector index permits.");
        }

        var indexedChunks = new List<DocumentVectorChunk>(
            textChunks.Count);

        var allowedPrincipalIds =
            request.AllowedPrincipalIds.ToArray();

        var allowedSecurityGroups =
            request.AllowedSecurityGroups.ToArray();

        for (var offset = 0;
             offset < textChunks.Count;
             offset += descriptor.MaximumBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchSize = Math.Min(
                descriptor.MaximumBatchSize,
                textChunks.Count - offset);

            var embeddingRequests =
                new List<TextEmbeddingRequest>(batchSize);

            for (var index = 0;
                 index < batchSize;
                 index++)
            {
                var chunkNumber = offset + index;

                var chunkId = CreateChunkId(
                    chunkNumber);

                embeddingRequests.Add(
                    new TextEmbeddingRequest(
                        RequestId: chunkId,
                        Text: textChunks[chunkNumber]));
            }

            var batchRequest =
                new TextEmbeddingBatchRequest(
                    embeddingRequests);

            EmbeddingValidation.ValidateBatchRequest(
                batchRequest,
                descriptor);

            var batchResult =
                await _embeddingProvider.GenerateBatchAsync(
                    batchRequest,
                    cancellationToken);

            EmbeddingValidation.ValidateBatchResult(
                batchRequest,
                batchResult,
                descriptor);

            for (var index = 0;
                 index < batchResult.Items.Count;
                 index++)
            {
                var chunkNumber = offset + index;

                var embedding = batchResult.Items[index];

                var chunkId = CreateChunkId(
                    chunkNumber);

                var citation = new DocumentSearchCitation(
                    DocumentId: request.DocumentId,
                    ChunkId: chunkId,
                    DocumentTitle: request.DocumentTitle,
                    PageNumber: null,
                    SectionHeading: null);

                var vectorChunk = new DocumentVectorChunk(
                    DocumentId: request.DocumentId,
                    TenantId: request.TenantId,
                    ChunkId: chunkId,
                    Text: textChunks[chunkNumber],
                    Vector: embedding.Vector.ToArray(),
                    EmbeddingModel: descriptor.Model,
                    DocumentVersion: request.DocumentVersion,
                    Citation: citation,
                    AllowedPrincipalIds: allowedPrincipalIds,
                    AllowedSecurityGroups: allowedSecurityGroups);

                DocumentVectorIndexValidation.ValidateChunk(
                    vectorChunk,
                    descriptor.Dimensions);

                indexedChunks.Add(vectorChunk);
            }
        }

        var replaceRequest =
            new ReplaceDocumentVectorsRequest(
                DocumentId: request.DocumentId,
                TenantId: request.TenantId,
                NewDocumentVersion: request.DocumentVersion,
                ExpectedIndexedVersion:
                    request.ExpectedIndexedVersion,
                Chunks: indexedChunks);

        DocumentVectorIndexValidation.ValidateReplaceRequest(
            replaceRequest,
            descriptor.Dimensions);

        cancellationToken.ThrowIfCancellationRequested();

        await _vectorIndex.ReplaceDocumentAsync(
            replaceRequest,
            cancellationToken);

        return new IndexDocumentContentResult(
            DocumentId: request.DocumentId,
            TenantId: request.TenantId,
            DocumentVersion: request.DocumentVersion,
            ChunkCount: indexedChunks.Count,
            EmbeddingModel: descriptor.Model,
            EmbeddingDimensions: descriptor.Dimensions);
    }

    private void ValidateRequest(
        IndexDocumentContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(request));
        }

        ValidateIdentifier(
            request.TenantId,
            nameof(request.TenantId));

        ValidateVersion(
            request.DocumentVersion,
            nameof(request.DocumentVersion));

        if (request.ExpectedIndexedVersion is not null)
        {
            ValidateVersion(
                request.ExpectedIndexedVersion,
                nameof(request.ExpectedIndexedVersion));
        }

        ArgumentNullException.ThrowIfNull(
            request.Content);

        if (request.Content.Length >
            _options.MaximumDocumentCharacters)
        {
            throw new ArgumentException(
                "Document content exceeds the indexing limit.",
                nameof(request));
        }

        if (request.Content.Contains('\0'))
        {
            throw new ArgumentException(
                "Document content contains an invalid null character.",
                nameof(request));
        }

        if (request.DocumentTitle is not null)
        {
            if (request.DocumentTitle.Length > 512 ||
                request.DocumentTitle.Any(char.IsControl))
            {
                throw new ArgumentException(
                    "Document title is invalid.",
                    nameof(request));
            }
        }

        ValidateAcl(
            request.AllowedPrincipalIds,
            nameof(request.AllowedPrincipalIds));

        ValidateAcl(
            request.AllowedSecurityGroups,
            nameof(request.AllowedSecurityGroups));
    }

    private static IReadOnlyList<string> SplitIntoChunks(
        string content,
        int chunkSize,
        int overlap)
    {
        var chunks = new List<string>();

        if (string.IsNullOrWhiteSpace(content))
        {
            return chunks;
        }

        var position = 0;

        while (position < content.Length)
        {
            var remaining = content.Length - position;

            var length = Math.Min(
                chunkSize,
                remaining);

            var candidate = content.Substring(
                position,
                length);

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                chunks.Add(candidate);
            }

            if (position + length >= content.Length)
            {
                break;
            }

            var nextPosition = position + length - overlap;

            if (nextPosition <= position)
            {
                throw new InvalidOperationException(
                    "Document chunking failed to advance.");
            }

            position = nextPosition;
        }

        return chunks;
    }

    private static string CreateChunkId(
        int chunkNumber)
    {
        return "chunk-" +
            chunkNumber.ToString(
                "D6",
                CultureInfo.InvariantCulture);
    }

    private static void ValidateOptions(
        DocumentContentIndexingOptions options)
    {
        if (options.ChunkSizeCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Chunk size must be positive.");
        }

        if (options.ChunkOverlapCharacters < 0 ||
            options.ChunkOverlapCharacters >=
                options.ChunkSizeCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Chunk overlap must be nonnegative and smaller than chunk size.");
        }

        if (options.MaximumDocumentCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum document size must be positive.");
        }
    }

    private static void ValidateAcl(
        IReadOnlyList<string> entries,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count >
            DocumentVectorIndexValidation.MaximumAclEntries)
        {
            throw new ArgumentException(
                "Document ACL exceeds the permitted size.",
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
                    "Document ACL contains duplicate entries.",
                    parameterName);
            }
        }
    }

    private static void ValidateVersion(
        string version,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            version,
            parameterName);

        if (version.Length >
            DocumentVectorIndexValidation.MaximumDocumentVersionCharacters)
        {
            throw new ArgumentException(
                "Document version exceeds the permitted length.",
                parameterName);
        }

        if (version != version.Trim() ||
            version.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Document version must be normalized.",
                parameterName);
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