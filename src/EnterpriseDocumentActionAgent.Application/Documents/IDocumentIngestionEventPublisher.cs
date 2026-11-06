namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Describes the outcome of submitting a document
/// ingestion event for durable publication.
/// </summary>
public enum DocumentIngestionEventPublishOutcome
{
    /// <summary>
    /// The event was durably accepted for publication.
    /// </summary>
    Published = 1,

    /// <summary>
    /// The same event was already durably accepted.
    /// </summary>
    AlreadyPublished = 2
}

/// <summary>
/// Represents the result of publishing a document
/// ingestion event.
/// </summary>
/// <remarks>
/// Publication means that the event has been durably
/// accepted by the configured publisher.
///
/// For a transactional outbox implementation, this
/// means the event has been stored in the outbox.
///
/// It does not mean that an indexing worker has
/// processed the event.
///
/// If publication participates in a caller-owned
/// database transaction, durability is conditional
/// on that transaction being committed.
/// </remarks>
public sealed record DocumentIngestionEventPublishResult(
    Guid EventId,
    Guid DocumentId,
    string TenantId,
    DocumentIngestionEventPublishOutcome Outcome);

/// <summary>
/// Defines an application-facing publisher for
/// document ingestion events.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Accept only validated ingestion events.
/// - Preserve the original event identifier.
/// - Preserve document and tenant identifiers.
/// - Preserve event type and content version.
/// - Honor cancellation.
/// - Propagate publication failures.
/// - Support idempotent event submission.
///
/// Implementations must not:
///
/// - Silently discard events.
/// - Modify event identity.
/// - Include document content in diagnostics.
/// - Treat outbox persistence as completed indexing.
///
/// This abstraction does not prescribe a particular
/// message broker, database, or transport.
/// </remarks>
public interface IDocumentIngestionEventPublisher
{
    /// <summary>
    /// Publishes a document ingestion event.
    /// </summary>
    /// <param name="ingestionEvent">
    /// The immutable event to publish.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token for the publication operation.
    /// </param>
    /// <returns>
    /// The durable publication outcome.
    /// </returns>
    Task<DocumentIngestionEventPublishResult> PublishAsync(
        DocumentIngestionEvent ingestionEvent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides validation for document ingestion
/// event publication results.
/// </summary>
public static class DocumentIngestionEventPublicationValidation
{
    public const int MaximumIdentifierCharacters = 256;
    public const int MaximumContentVersionCharacters = 256;

    /// <summary>
    /// Validates an ingestion event before publication.
    /// </summary>
    public static void ValidateEvent(
        DocumentIngestionEvent ingestionEvent)
    {
        ArgumentNullException.ThrowIfNull(
            ingestionEvent);

        if (ingestionEvent.EventId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid event identifier is required.",
                nameof(ingestionEvent));
        }

        if (ingestionEvent.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(ingestionEvent));
        }

        ValidateIdentifier(
            ingestionEvent.TenantId,
            nameof(ingestionEvent.TenantId));

        ValidateIdentifier(
            ingestionEvent.InitiatedBy,
            nameof(ingestionEvent.InitiatedBy));

        if (!Enum.IsDefined(ingestionEvent.EventType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ingestionEvent),
                "The ingestion event type is unsupported.");
        }

        ValidateContentVersion(
            ingestionEvent.ContentVersion);

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

        if (ingestionEvent.OccurredAtUtc == default ||
            ingestionEvent.OccurredAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Ingestion event timestamp must be UTC.",
                nameof(ingestionEvent));
        }
    }

    /// <summary>
    /// Validates the result of a publication attempt.
    /// </summary>
    public static void ValidateResult(
        DocumentIngestionEvent ingestionEvent,
        DocumentIngestionEventPublishResult result)
    {
        ValidateEvent(ingestionEvent);

        ArgumentNullException.ThrowIfNull(result);

        if (result.EventId != ingestionEvent.EventId)
        {
            throw new ArgumentException(
                "Published event identifier does not match the request.",
                nameof(result));
        }

        if (result.DocumentId != ingestionEvent.DocumentId)
        {
            throw new ArgumentException(
                "Published document identifier does not match the request.",
                nameof(result));
        }

        if (!string.Equals(
                result.TenantId,
                ingestionEvent.TenantId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Published tenant identifier does not match the request.",
                nameof(result));
        }

        if (!Enum.IsDefined(result.Outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                "The publication outcome is unsupported.");
        }
    }

    /// <summary>
    /// Validates a publication result independently
    /// of its original ingestion event.
    /// </summary>
    public static void ValidateResult(
        DocumentIngestionEventPublishResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.EventId == Guid.Empty)
        {
            throw new ArgumentException(
                "Published event identifier is invalid.",
                nameof(result));
        }

        if (result.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Published document identifier is invalid.",
                nameof(result));
        }

        ValidateIdentifier(
            result.TenantId,
            nameof(result.TenantId));

        if (!Enum.IsDefined(result.Outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(result),
                "The publication outcome is unsupported.");
        }
    }

    private static void ValidateContentVersion(
        string? contentVersion)
    {
        if (contentVersion is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(contentVersion) ||
            contentVersion.Length >
                MaximumContentVersionCharacters ||
            contentVersion != contentVersion.Trim() ||
            contentVersion.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Content version is invalid.",
                nameof(contentVersion));
        }
    }

    private static void ValidateIdentifier(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);

        if (value.Length > MaximumIdentifierCharacters ||
            value != value.Trim() ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Identifier is invalid.",
                parameterName);
        }
    }
}