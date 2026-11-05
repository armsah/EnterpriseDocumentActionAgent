using EnterpriseDocumentActionAgent.Application.Documents;

namespace EnterpriseDocumentActionAgent.Application.Indexing;

/// <summary>
/// Represents a durable checkpoint for a completed
/// document ingestion event.
/// </summary>
/// <remarks>
/// A checkpoint identifies one ingestion event that
/// has reached a terminal processing outcome.
///
/// EventId is the idempotency key.
///
/// TenantId and DocumentId are retained to support
/// tenant-scoped auditing and consistency checks.
///
/// ContentVersion may be null for metadata-update
/// and deletion events.
///
/// This record must not contain document text,
/// embedding vectors, credentials, or other
/// sensitive document content.
/// </remarks>
public sealed record ProcessedDocumentIndexEvent(
    Guid EventId,
    Guid DocumentId,
    string TenantId,
    DocumentIngestionEventType EventType,
    PublishedDocumentIndexOutcome Outcome,
    string? ContentVersion,
    int IndexedChunkCount,
    DateTimeOffset ProcessedAtUtc);

/// <summary>
/// Describes the result of attempting to record
/// a completed ingestion event.
/// </summary>
public enum ProcessedDocumentIndexEventWriteOutcome
{
    Recorded = 1,
    AlreadyRecorded = 2
}

/// <summary>
/// Represents the outcome of writing a durable
/// processing checkpoint.
/// </summary>
public sealed record ProcessedDocumentIndexEventWriteResult(
    ProcessedDocumentIndexEvent Checkpoint,
    ProcessedDocumentIndexEventWriteOutcome Outcome);

/// <summary>
/// Defines durable storage for completed document
/// ingestion event checkpoints.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Enforce tenant isolation.
/// - Use EventId as a unique idempotency key.
/// - Store checkpoints durably.
/// - Make duplicate writes idempotent.
/// - Reject conflicting data for an existing EventId.
/// - Use atomic database operations to prevent races.
/// - Honor cancellation.
///
/// An existence check followed by a write is not
/// sufficient to guarantee exactly-once processing.
///
/// The worker must also ensure that document indexing
/// and deletion operations are idempotent.
/// </remarks>
public interface IProcessedDocumentIndexEventStore
{
    /// <summary>
    /// Retrieves a completed checkpoint by event identity.
    /// </summary>
    Task<ProcessedDocumentIndexEvent?> GetAsync(
        Guid eventId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically records a completed checkpoint
    /// or returns an existing matching checkpoint.
    /// </summary>
    Task<ProcessedDocumentIndexEventWriteResult> RecordAsync(
        ProcessedDocumentIndexEvent checkpoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates validated processing checkpoints from
/// ingestion events and processing results.
/// </summary>
public interface IProcessedDocumentIndexEventFactory
{
    ProcessedDocumentIndexEvent Create(
        DocumentIngestionEvent ingestionEvent,
        PublishedDocumentIndexResult processingResult);
}

/// <summary>
/// Default factory for completed document indexing
/// event checkpoints.
/// </summary>
public sealed class ProcessedDocumentIndexEventFactory
    : IProcessedDocumentIndexEventFactory
{
    private readonly TimeProvider _timeProvider;

    public ProcessedDocumentIndexEventFactory(
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider
            ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ProcessedDocumentIndexEvent Create(
        DocumentIngestionEvent ingestionEvent,
        PublishedDocumentIndexResult processingResult)
    {
        ProcessedDocumentIndexEventValidation.ValidateEvent(
            ingestionEvent);

        ProcessedDocumentIndexEventValidation.ValidateResult(
            ingestionEvent,
            processingResult);

        var checkpoint = new ProcessedDocumentIndexEvent(
            EventId: ingestionEvent.EventId,
            DocumentId: ingestionEvent.DocumentId,
            TenantId: ingestionEvent.TenantId,
            EventType: ingestionEvent.EventType,
            Outcome: processingResult.Outcome,
            ContentVersion: ingestionEvent.ContentVersion,
            IndexedChunkCount: processingResult.IndexedChunkCount,
            ProcessedAtUtc: _timeProvider.GetUtcNow());

        ProcessedDocumentIndexEventValidation.ValidateCheckpoint(
            checkpoint);

        return checkpoint;
    }
}

/// <summary>
/// Provides structural validation for ingestion
/// processing checkpoints and related results.
/// </summary>
/// <remarks>
/// These checks do not replace database constraints,
/// transactional writes, tenant-scoped queries,
/// or concurrency control.
/// </remarks>
public static class ProcessedDocumentIndexEventValidation
{
    public const int MaximumIdentifierCharacters = 256;
    public const int MaximumContentVersionCharacters = 256;

    /// <summary>
    /// Validates an ingestion event before checkpointing.
    /// </summary>
    public static void ValidateEvent(
        DocumentIngestionEvent ingestionEvent)
    {
        ArgumentNullException.ThrowIfNull(
            ingestionEvent);

        ValidateEventId(ingestionEvent.EventId);

        ValidateDocumentId(ingestionEvent.DocumentId);

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
                "Ingestion event type is invalid.");
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
                    "Content version is required for this event type.",
                    nameof(ingestionEvent));
            }
        }

        if (ingestionEvent.EventType ==
                DocumentIngestionEventType.Deleted &&
            ingestionEvent.ContentVersion is not null)
        {
            throw new ArgumentException(
                "Deletion events cannot specify a content version.",
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
    /// Validates that a processing result corresponds
    /// to the ingestion event being checkpointed.
    /// </summary>
    public static void ValidateResult(
        DocumentIngestionEvent ingestionEvent,
        PublishedDocumentIndexResult processingResult)
    {
        ValidateEvent(ingestionEvent);

        ArgumentNullException.ThrowIfNull(
            processingResult);

        if (processingResult.EventId !=
                ingestionEvent.EventId ||
            processingResult.DocumentId !=
                ingestionEvent.DocumentId ||
            !string.Equals(
                processingResult.TenantId,
                ingestionEvent.TenantId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Processing result does not match the ingestion event.",
                nameof(processingResult));
        }

        ValidateOutcome(processingResult.Outcome);

        if (processingResult.IndexedChunkCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processingResult),
                "Indexed chunk count cannot be negative.");
        }

        ValidateContentVersion(
            processingResult.DocumentVersion);

        if (processingResult.Outcome ==
                PublishedDocumentIndexOutcome.Indexed)
        {
            if (processingResult.DocumentVersion is null)
            {
                throw new ArgumentException(
                    "Indexed results require a document version.",
                    nameof(processingResult));
            }

            if (ingestionEvent.EventType ==
                DocumentIngestionEventType.Deleted)
            {
                throw new ArgumentException(
                    "Deletion events cannot produce indexed outcomes.",
                    nameof(processingResult));
            }
        }
        else if (processingResult.IndexedChunkCount != 0)
        {
            throw new ArgumentException(
                "Non-indexed outcomes cannot report indexed chunks.",
                nameof(processingResult));
        }

        if (processingResult.Outcome ==
                PublishedDocumentIndexOutcome.Deleted &&
            ingestionEvent.EventType !=
                DocumentIngestionEventType.Deleted)
        {
            throw new ArgumentException(
                "Only deletion events can produce deleted outcomes.",
                nameof(processingResult));
        }
    }

    /// <summary>
    /// Validates a stored processing checkpoint.
    /// </summary>
    public static void ValidateCheckpoint(
        ProcessedDocumentIndexEvent checkpoint)
    {
        ArgumentNullException.ThrowIfNull(
            checkpoint);

        ValidateEventId(checkpoint.EventId);

        ValidateDocumentId(checkpoint.DocumentId);

        ValidateIdentifier(
            checkpoint.TenantId,
            nameof(checkpoint.TenantId));

        if (!Enum.IsDefined(checkpoint.EventType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(checkpoint),
                "Checkpoint event type is invalid.");
        }

        ValidateOutcome(checkpoint.Outcome);

        ValidateContentVersion(
            checkpoint.ContentVersion);

        if (checkpoint.EventType is
                DocumentIngestionEventType.Created or
                DocumentIngestionEventType.ContentUpdated)
        {
            if (checkpoint.ContentVersion is null)
            {
                throw new ArgumentException(
                    "Checkpoint requires a content version.",
                    nameof(checkpoint));
            }
        }

        if (checkpoint.EventType ==
                DocumentIngestionEventType.Deleted &&
            checkpoint.ContentVersion is not null)
        {
            throw new ArgumentException(
                "Deletion checkpoints cannot specify a content version.",
                nameof(checkpoint));
        }

        if (checkpoint.IndexedChunkCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(checkpoint),
                "Indexed chunk count cannot be negative.");
        }

        if (checkpoint.Outcome !=
                PublishedDocumentIndexOutcome.Indexed &&
            checkpoint.IndexedChunkCount != 0)
        {
            throw new ArgumentException(
                "Non-indexed checkpoints cannot report indexed chunks.",
                nameof(checkpoint));
        }

        if (checkpoint.ProcessedAtUtc == default ||
            checkpoint.ProcessedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Checkpoint timestamp must be UTC.",
                nameof(checkpoint));
        }
    }

    private static void ValidateOutcome(
        PublishedDocumentIndexOutcome outcome)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                "Processing outcome is invalid.");
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

    private static void ValidateEventId(
        Guid eventId)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid event identifier is required.",
                nameof(eventId));
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