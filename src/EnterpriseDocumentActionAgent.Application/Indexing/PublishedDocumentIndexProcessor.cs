using EnterpriseDocumentActionAgent.Application.Documents;
using EnterpriseDocumentActionAgent.Application.Search;

namespace EnterpriseDocumentActionAgent.Application.Indexing;

/// <summary>
/// Represents a trusted snapshot of a document
/// prepared for search indexing.
/// </summary>
/// <remarks>
/// The snapshot must be loaded from trusted storage.
///
/// Content must contain extracted plain text.
///
/// ACLs must reflect the permissions applicable
/// to the document version being indexed.
///
/// The snapshot reader must not return data from
/// another tenant.
/// </remarks>
public sealed record PublishedDocumentIndexSnapshot(
    Guid DocumentId,
    string TenantId,
    string DocumentVersion,
    string Content,
    string? DocumentTitle,
    IReadOnlyList<string> AllowedPrincipalIds,
    IReadOnlyList<string> AllowedSecurityGroups,
    string? ExpectedIndexedVersion);

/// <summary>
/// Defines the trusted document snapshot reader
/// used by the indexing processor.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Enforce tenant isolation during reads.
/// - Return a consistent document snapshot.
/// - Include the current document version.
/// - Include the current effective document ACL.
/// - Avoid returning deleted documents.
/// - Honor cancellation.
///
/// Returning null means that the document is
/// unavailable for indexing.
/// </remarks>
public interface IPublishedDocumentIndexSnapshotReader
{
    Task<PublishedDocumentIndexSnapshot?> GetAsync(
        Guid documentId,
        string tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines possible outcomes of processing
/// a document ingestion event.
/// </summary>
public enum PublishedDocumentIndexOutcome
{
    Indexed = 1,
    Deleted = 2,
    SkippedStale = 3,
    SkippedUnavailable = 4
}

/// <summary>
/// Summarizes processing of a document ingestion event.
/// </summary>
public sealed record PublishedDocumentIndexResult(
    Guid EventId,
    Guid DocumentId,
    string TenantId,
    PublishedDocumentIndexOutcome Outcome,
    string? DocumentVersion,
    int IndexedChunkCount);

/// <summary>
/// Defines the application-facing ingestion
/// event processor.
/// </summary>
public interface IPublishedDocumentIndexProcessor
{
    Task<PublishedDocumentIndexResult> ProcessAsync(
        DocumentIngestionEvent ingestionEvent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates published document ingestion events
/// with the document indexing pipeline.
/// </summary>
/// <remarks>
/// Created, ContentUpdated, and MetadataUpdated
/// events trigger indexing of a trusted snapshot.
///
/// Deleted events remove the document's indexed
/// vectors.
///
/// The processor does not deserialize arbitrary
/// document content from the event payload.
///
/// The processor does not authenticate event publishers.
///
/// The event transport must guarantee that only
/// trusted, validated events reach this component.
///
/// The vector index must enforce atomic replacement
/// and prevent stale writes.
/// </remarks>
public sealed class PublishedDocumentIndexProcessor
    : IPublishedDocumentIndexProcessor
{
    private readonly IPublishedDocumentIndexSnapshotReader
        _snapshotReader;

    private readonly IDocumentContentIndexer
        _contentIndexer;

    private readonly IDocumentVectorIndex
        _vectorIndex;

    public PublishedDocumentIndexProcessor(
        IPublishedDocumentIndexSnapshotReader snapshotReader,
        IDocumentContentIndexer contentIndexer,
        IDocumentVectorIndex vectorIndex)
    {
        _snapshotReader = snapshotReader
            ?? throw new ArgumentNullException(
                nameof(snapshotReader));

        _contentIndexer = contentIndexer
            ?? throw new ArgumentNullException(
                nameof(contentIndexer));

        _vectorIndex = vectorIndex
            ?? throw new ArgumentNullException(
                nameof(vectorIndex));
    }

    /// <inheritdoc />
    public async Task<PublishedDocumentIndexResult> ProcessAsync(
        DocumentIngestionEvent ingestionEvent,
        CancellationToken cancellationToken = default)
    {
        ValidateEvent(ingestionEvent);

        cancellationToken.ThrowIfCancellationRequested();

        return ingestionEvent.EventType switch
        {
            DocumentIngestionEventType.Created =>
                await ProcessIndexEventAsync(
                    ingestionEvent,
                    cancellationToken),

            DocumentIngestionEventType.ContentUpdated =>
                await ProcessIndexEventAsync(
                    ingestionEvent,
                    cancellationToken),

            DocumentIngestionEventType.MetadataUpdated =>
                await ProcessIndexEventAsync(
                    ingestionEvent,
                    cancellationToken),

            DocumentIngestionEventType.Deleted =>
                await ProcessDeleteEventAsync(
                    ingestionEvent,
                    cancellationToken),

            _ => throw new ArgumentOutOfRangeException(
                nameof(ingestionEvent),
                "Unsupported document ingestion event type.")
        };
    }

    private async Task<PublishedDocumentIndexResult>
        ProcessIndexEventAsync(
            DocumentIngestionEvent ingestionEvent,
            CancellationToken cancellationToken)
    {
        var snapshot = await _snapshotReader.GetAsync(
            ingestionEvent.DocumentId,
            ingestionEvent.TenantId,
            cancellationToken);

        if (snapshot is null)
        {
            return CreateResult(
                ingestionEvent,
                PublishedDocumentIndexOutcome.SkippedUnavailable);
        }

        ValidateSnapshot(
            ingestionEvent,
            snapshot);

        if (ingestionEvent.ContentVersion is not null &&
            !string.Equals(
                snapshot.DocumentVersion,
                ingestionEvent.ContentVersion,
                StringComparison.Ordinal))
        {
            return CreateResult(
                ingestionEvent,
                PublishedDocumentIndexOutcome.SkippedStale);
        }

        var indexRequest = new IndexDocumentContentRequest(
            DocumentId: snapshot.DocumentId,
            TenantId: snapshot.TenantId,
            DocumentVersion: snapshot.DocumentVersion,
            Content: snapshot.Content,
            DocumentTitle: snapshot.DocumentTitle,
            AllowedPrincipalIds: snapshot.AllowedPrincipalIds,
            AllowedSecurityGroups: snapshot.AllowedSecurityGroups,
            ExpectedIndexedVersion:
                snapshot.ExpectedIndexedVersion);

        var indexResult = await _contentIndexer.IndexAsync(
            indexRequest,
            cancellationToken);

        return new PublishedDocumentIndexResult(
            EventId: ingestionEvent.EventId,
            DocumentId: ingestionEvent.DocumentId,
            TenantId: ingestionEvent.TenantId,
            Outcome: PublishedDocumentIndexOutcome.Indexed,
            DocumentVersion: indexResult.DocumentVersion,
            IndexedChunkCount: indexResult.ChunkCount);
    }

    private async Task<PublishedDocumentIndexResult>
        ProcessDeleteEventAsync(
            DocumentIngestionEvent ingestionEvent,
            CancellationToken cancellationToken)
    {
        var deleteRequest = new DeleteDocumentVectorsRequest(
            DocumentId: ingestionEvent.DocumentId,
            TenantId: ingestionEvent.TenantId);

        DocumentVectorIndexValidation.ValidateDeleteRequest(
            deleteRequest);

        await _vectorIndex.DeleteDocumentAsync(
            deleteRequest,
            cancellationToken);

        return CreateResult(
            ingestionEvent,
            PublishedDocumentIndexOutcome.Deleted);
    }

    private static PublishedDocumentIndexResult CreateResult(
        DocumentIngestionEvent ingestionEvent,
        PublishedDocumentIndexOutcome outcome)
    {
        return new PublishedDocumentIndexResult(
            EventId: ingestionEvent.EventId,
            DocumentId: ingestionEvent.DocumentId,
            TenantId: ingestionEvent.TenantId,
            Outcome: outcome,
            DocumentVersion: ingestionEvent.ContentVersion,
            IndexedChunkCount: 0);
    }

    private static void ValidateEvent(
        DocumentIngestionEvent ingestionEvent)
    {
        ArgumentNullException.ThrowIfNull(
            ingestionEvent);

        if (ingestionEvent.EventId == Guid.Empty)
        {
            throw new ArgumentException(
                "Ingestion event identifier is invalid.",
                nameof(ingestionEvent));
        }

        if (ingestionEvent.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Ingestion document identifier is invalid.",
                nameof(ingestionEvent));
        }

        ValidateIdentifier(
            ingestionEvent.TenantId,
            nameof(ingestionEvent.TenantId));

        if (ingestionEvent.ContentVersion is not null)
        {
            ValidateVersion(
                ingestionEvent.ContentVersion);
        }

        if (ingestionEvent.EventType is
                DocumentIngestionEventType.Created or
                DocumentIngestionEventType.ContentUpdated)
        {
            if (ingestionEvent.ContentVersion is null)
            {
                throw new ArgumentException(
                    "Creation and content-update events require a content version.",
                    nameof(ingestionEvent));
            }
        }

        if (ingestionEvent.EventType ==
                DocumentIngestionEventType.Deleted &&
            ingestionEvent.ContentVersion is not null)
        {
            throw new ArgumentException(
                "Deletion events must not specify a content version.",
                nameof(ingestionEvent));
        }

        if (!Enum.IsDefined(
                ingestionEvent.EventType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ingestionEvent),
                "Ingestion event type is invalid.");
        }
    }

    private static void ValidateSnapshot(
        DocumentIngestionEvent ingestionEvent,
        PublishedDocumentIndexSnapshot snapshot)
    {
        if (snapshot.DocumentId !=
            ingestionEvent.DocumentId)
        {
            throw new InvalidOperationException(
                "Document snapshot identifier does not match the event.");
        }

        if (!string.Equals(
                snapshot.TenantId,
                ingestionEvent.TenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Document snapshot violates tenant isolation.");
        }

        ValidateVersion(
            snapshot.DocumentVersion);

        ArgumentNullException.ThrowIfNull(
            snapshot.Content);

        ArgumentNullException.ThrowIfNull(
            snapshot.AllowedPrincipalIds);

        ArgumentNullException.ThrowIfNull(
            snapshot.AllowedSecurityGroups);
    }

    private static void ValidateVersion(
        string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            version);

        if (version.Length >
            DocumentVectorIndexValidation.MaximumDocumentVersionCharacters ||
            version != version.Trim() ||
            version.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Document version is invalid.",
                nameof(version));
        }
    }

    private static void ValidateIdentifier(
        string identifier,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            identifier,
            parameterName);

        if (identifier.Length >
            DocumentSearchValidation.MaximumIdentifierCharacters ||
            identifier != identifier.Trim() ||
            identifier.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Document identifier is invalid.",
                parameterName);
        }
    }
}