using EnterpriseDocumentActionAgent.Application.Documents;
using EnterpriseDocumentActionAgent.Application.Indexing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EnterpriseDocumentActionAgent.Workers.Indexing;

/// <summary>
/// Configures the document indexing background worker.
/// </summary>
public sealed record DocumentIndexWorkerOptions(
    TimeSpan PollInterval,
    TimeSpan FailureDelay)
{
    public static DocumentIndexWorkerOptions Default { get; } =
        new(
            PollInterval: TimeSpan.FromSeconds(5),
            FailureDelay: TimeSpan.FromSeconds(10));
}

/// <summary>
/// Represents a leased ingestion event received
/// from the durable event source.
/// </summary>
/// <remarks>
/// LeaseToken identifies the current delivery lease.
///
/// The event source must reject acknowledgements
/// and retries made with an expired or invalid lease.
///
/// DeliveryAttempt is informational and must not
/// be used as an authorization mechanism.
/// </remarks>
public sealed record LeasedDocumentIndexEvent(
    DocumentIngestionEvent IngestionEvent,
    string LeaseToken,
    int DeliveryAttempt);

/// <summary>
/// Defines a durable source of document ingestion
/// events for the indexing worker.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Enforce tenant isolation.
/// - Atomically lease events.
/// - Prevent concurrent active leases.
/// - Honor cancellation.
/// - Persist acknowledgements.
/// - Schedule failed events for retry.
/// - Reject expired lease tokens.
/// - Support poison-message handling.
///
/// A PostgreSQL implementation may use
/// SELECT FOR UPDATE SKIP LOCKED together
/// with durable lease state.
/// </remarks>
public interface IDocumentIndexEventSource
{
    /// <summary>
    /// Attempts to lease the next available event.
    /// </summary>
    Task<LeasedDocumentIndexEvent?> LeaseNextAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges a successfully handled event.
    /// </summary>
    Task AcknowledgeAsync(
        LeasedDocumentIndexEvent leasedEvent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases or reschedules an event after failure.
    /// </summary>
    Task RetryAsync(
        LeasedDocumentIndexEvent leasedEvent,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Background service that processes published
/// document ingestion events.
/// </summary>
/// <remarks>
/// Processing order:
///
/// 1. Lease an event.
/// 2. Validate event identity.
/// 3. Check durable completion checkpoint.
/// 4. Process the event when not yet completed.
/// 5. Record a completion checkpoint.
/// 6. Acknowledge the event.
///
/// If processing fails, the worker requests a retry.
///
/// Checkpoint persistence and acknowledgement
/// are separate operations. This intentionally
/// provides at-least-once delivery semantics.
///
/// The indexing processor and vector-index adapter
/// must support safe replay.
/// </remarks>
public sealed class DocumentIndexWorker : BackgroundService
{
    private readonly IDocumentIndexEventSource _eventSource;

    private readonly IPublishedDocumentIndexProcessor
        _indexProcessor;

    private readonly IProcessedDocumentIndexEventStore
        _checkpointStore;

    private readonly IProcessedDocumentIndexEventFactory
        _checkpointFactory;

    private readonly ILogger<DocumentIndexWorker> _logger;

    private readonly DocumentIndexWorkerOptions _options;

    public DocumentIndexWorker(
        IDocumentIndexEventSource eventSource,
        IPublishedDocumentIndexProcessor indexProcessor,
        IProcessedDocumentIndexEventStore checkpointStore,
        IProcessedDocumentIndexEventFactory checkpointFactory,
        ILogger<DocumentIndexWorker> logger,
        DocumentIndexWorkerOptions? options = null)
    {
        _eventSource = eventSource
            ?? throw new ArgumentNullException(
                nameof(eventSource));

        _indexProcessor = indexProcessor
            ?? throw new ArgumentNullException(
                nameof(indexProcessor));

        _checkpointStore = checkpointStore
            ?? throw new ArgumentNullException(
                nameof(checkpointStore));

        _checkpointFactory = checkpointFactory
            ?? throw new ArgumentNullException(
                nameof(checkpointFactory));

        _logger = logger
            ?? throw new ArgumentNullException(
                nameof(logger));

        _options = options
            ?? DocumentIndexWorkerOptions.Default;

        ValidateOptions(_options);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Document indexing worker started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var leasedEvent =
                        await _eventSource.LeaseNextAsync(
                            stoppingToken);

                    if (leasedEvent is null)
                    {
                        await Task.Delay(
                            _options.PollInterval,
                            stoppingToken);

                        continue;
                    }

                    await ProcessLeasedEventAsync(
                        leasedEvent,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Document indexing worker polling failed.");

                    await DelayAfterFailureAsync(
                        stoppingToken);
                }
            }
        }
        finally
        {
            _logger.LogInformation(
                "Document indexing worker stopped.");
        }
    }

    private async Task ProcessLeasedEventAsync(
        LeasedDocumentIndexEvent leasedEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateLease(leasedEvent);

            var ingestionEvent = leasedEvent.IngestionEvent;

            ProcessedDocumentIndexEventValidation.ValidateEvent(
                ingestionEvent);

            var existingCheckpoint =
                await _checkpointStore.GetAsync(
                    ingestionEvent.EventId,
                    ingestionEvent.TenantId,
                    cancellationToken);

            if (existingCheckpoint is not null)
            {
                ProcessedDocumentIndexEventValidation.ValidateCheckpoint(
                    existingCheckpoint);

                ValidateExistingCheckpoint(
                    ingestionEvent,
                    existingCheckpoint);

                _logger.LogInformation(
                    "Skipping previously processed document index event {EventId}.",
                    ingestionEvent.EventId);

                await _eventSource.AcknowledgeAsync(
                    leasedEvent,
                    cancellationToken);

                return;
            }

            var processingResult =
                await _indexProcessor.ProcessAsync(
                    ingestionEvent,
                    cancellationToken);

            var checkpoint = _checkpointFactory.Create(
                ingestionEvent,
                processingResult);

            var writeResult =
                await _checkpointStore.RecordAsync(
                    checkpoint,
                    cancellationToken);

            ValidateCheckpointWriteResult(
                checkpoint,
                writeResult);

            await _eventSource.AcknowledgeAsync(
                leasedEvent,
                cancellationToken);

            _logger.LogInformation(
                "Processed document index event {EventId} with outcome {Outcome}.",
                ingestionEvent.EventId,
                processingResult.Outcome);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Document index event processing failed.");

            await _eventSource.RetryAsync(
                leasedEvent,
                cancellationToken);
        }
    }

    private async Task DelayAfterFailureAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(
                _options.FailureDelay,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private static void ValidateLease(
        LeasedDocumentIndexEvent leasedEvent)
    {
        ArgumentNullException.ThrowIfNull(
            leasedEvent);

        ArgumentNullException.ThrowIfNull(
            leasedEvent.IngestionEvent);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            leasedEvent.LeaseToken);

        if (leasedEvent.LeaseToken.Length > 512 ||
            leasedEvent.LeaseToken !=
                leasedEvent.LeaseToken.Trim() ||
            leasedEvent.LeaseToken.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Document index event lease token is invalid.",
                nameof(leasedEvent));
        }

        if (leasedEvent.DeliveryAttempt <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leasedEvent),
                "Delivery attempt must be positive.");
        }
    }

    private static void ValidateExistingCheckpoint(
        DocumentIngestionEvent ingestionEvent,
        ProcessedDocumentIndexEvent checkpoint)
    {
        if (checkpoint.EventId != ingestionEvent.EventId ||
            checkpoint.DocumentId != ingestionEvent.DocumentId ||
            !string.Equals(
                checkpoint.TenantId,
                ingestionEvent.TenantId,
                StringComparison.Ordinal) ||
            checkpoint.EventType != ingestionEvent.EventType ||
            !string.Equals(
                checkpoint.ContentVersion,
                ingestionEvent.ContentVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Stored checkpoint conflicts with the ingestion event.");
        }
    }

    private static void ValidateCheckpointWriteResult(
        ProcessedDocumentIndexEvent expectedCheckpoint,
        ProcessedDocumentIndexEventWriteResult writeResult)
    {
        ArgumentNullException.ThrowIfNull(
            writeResult);

        if (!Enum.IsDefined(writeResult.Outcome))
        {
            throw new InvalidOperationException(
                "Checkpoint store returned an unsupported write outcome.");
        }

        var actualCheckpoint = writeResult.Checkpoint;

        ProcessedDocumentIndexEventValidation.ValidateCheckpoint(
            actualCheckpoint);

        if (actualCheckpoint.EventId !=
                expectedCheckpoint.EventId ||
            actualCheckpoint.DocumentId !=
                expectedCheckpoint.DocumentId ||
            !string.Equals(
                actualCheckpoint.TenantId,
                expectedCheckpoint.TenantId,
                StringComparison.Ordinal) ||
            actualCheckpoint.EventType !=
                expectedCheckpoint.EventType ||
            !string.Equals(
                actualCheckpoint.ContentVersion,
                expectedCheckpoint.ContentVersion,
                StringComparison.Ordinal) ||
            actualCheckpoint.Outcome !=
                expectedCheckpoint.Outcome ||
            actualCheckpoint.IndexedChunkCount !=
                expectedCheckpoint.IndexedChunkCount)
        {
            throw new InvalidOperationException(
                "Checkpoint store returned conflicting event data.");
        }
    }

    private static void ValidateOptions(
        DocumentIndexWorkerOptions options)
    {
        if (options.PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Polling interval must be positive.");
        }

        if (options.FailureDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Failure delay must be positive.");
        }
    }
}