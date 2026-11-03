namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Describes the type of document change that
/// requires ingestion processing.
/// </summary>
public enum DocumentIngestionEventType
{
    Created = 0,
    ContentUpdated = 1,
    MetadataUpdated = 2,
    Deleted = 3
}

/// <summary>
/// Represents a document change that may require
/// asynchronous indexing or index cleanup.
/// </summary>
/// <remarks>
/// Events must be processed within their tenant scope.
///
/// EventId supports deduplication and idempotency.
///
/// ContentVersion identifies the exact content version
/// associated with the event.
///
/// For deletion events, ContentVersion may be null
/// because the content may already be unavailable.
///
/// Events are immutable records and should be treated
/// as messages rather than mutable domain entities.
/// </remarks>
public sealed record DocumentIngestionEvent(
    Guid EventId,
    Guid DocumentId,
    string TenantId,
    DocumentIngestionEventType EventType,
    string? ContentVersion,
    string InitiatedBy,
    DateTimeOffset OccurredAtUtc);

/// <summary>
/// Represents a request to create a document
/// ingestion event.
/// </summary>
public sealed record CreateDocumentIngestionEventRequest(
    Guid DocumentId,
    string TenantId,
    DocumentIngestionEventType EventType,
    string? ContentVersion,
    string InitiatedBy);

/// <summary>
/// Defines a factory for creating validated
/// document ingestion events.
/// </summary>
public interface IDocumentIngestionEventFactory
{
    /// <summary>
    /// Creates a new document ingestion event.
    /// </summary>
    DocumentIngestionEvent Create(
        CreateDocumentIngestionEventRequest request);
}

/// <summary>
/// Creates validated document ingestion events.
/// </summary>
/// <remarks>
/// This factory does not publish events.
///
/// Event publication must occur through a
/// dedicated publisher implementation.
///
/// The hosting application must supply tenant
/// and initiator identities from trusted sources.
/// </remarks>
public sealed class DocumentIngestionEventFactory
    : IDocumentIngestionEventFactory
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates an ingestion event factory.
    /// </summary>
    public DocumentIngestionEventFactory(
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public DocumentIngestionEvent Create(
        CreateDocumentIngestionEventRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidateRequest(request);

        var tenantId = request.TenantId.Trim();
        var initiatedBy = request.InitiatedBy.Trim();

        var contentVersion = NormalizeOptionalText(
            request.ContentVersion);

        return new DocumentIngestionEvent(
            EventId: Guid.NewGuid(),
            DocumentId: request.DocumentId,
            TenantId: tenantId,
            EventType: request.EventType,
            ContentVersion: contentVersion,
            InitiatedBy: initiatedBy,
            OccurredAtUtc: _timeProvider.GetUtcNow());
    }

    private static void ValidateRequest(
        CreateDocumentIngestionEventRequest request)
    {
        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.TenantId);

        if (request.TenantId.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Tenant identifier cannot exceed 128 characters.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.InitiatedBy);

        if (request.InitiatedBy.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Initiator identifier cannot exceed 128 characters.",
                nameof(request));
        }

        if (!Enum.IsDefined(request.EventType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The ingestion event type is unsupported.");
        }

        ValidateContentVersion(request);
    }

    private static void ValidateContentVersion(
        CreateDocumentIngestionEventRequest request)
    {
        var contentVersion = NormalizeOptionalText(
            request.ContentVersion);

        if (request.ContentVersion is not null &&
            contentVersion is null)
        {
            throw new ArgumentException(
                "Content version cannot be empty.",
                nameof(request));
        }

        if (contentVersion is not null &&
            contentVersion.Length > 256)
        {
            throw new ArgumentException(
                "Content version cannot exceed 256 characters.",
                nameof(request));
        }

        // Events that require content processing must
        // identify the exact version to be processed.
        if (request.EventType is
                DocumentIngestionEventType.Created or
                DocumentIngestionEventType.ContentUpdated)
        {
            if (contentVersion is null)
            {
                throw new ArgumentException(
                    "Content version is required for " +
                    "creation and content-update events.",
                    nameof(request));
            }
        }

        // A deletion event must not depend on
        // content that may already be unavailable.
        if (request.EventType ==
                DocumentIngestionEventType.Deleted &&
            contentVersion is not null)
        {
            throw new ArgumentException(
                "Deletion events must not specify " +
                "a content version.",
                nameof(request));
        }
    }

    private static string? NormalizeOptionalText(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}