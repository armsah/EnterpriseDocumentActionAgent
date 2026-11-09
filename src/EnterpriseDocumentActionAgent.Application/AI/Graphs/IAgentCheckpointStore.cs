namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Represents a serialized checkpoint
/// for an agent graph execution.
/// </summary>
/// <remarks>
/// The payload is expected to contain
/// a serialized graph checkpoint.
///
/// The persistence layer treats the payload
/// as opaque application data.
///
/// This record does not deserialize,
/// authenticate, encrypt, or validate
/// the internal checkpoint schema.
/// </remarks>
public sealed record AgentCheckpointRecord
{
    /// <summary>
    /// Gets the unique checkpoint identifier.
    /// </summary>
    public Guid CheckpointId { get; }

    /// <summary>
    /// Gets the associated execution identifier.
    /// </summary>
    public Guid ExecutionId { get; }

    /// <summary>
    /// Gets the graph identifier.
    /// </summary>
    public string GraphId { get; }

    /// <summary>
    /// Gets the checkpoint sequence number.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the serialized checkpoint payload.
    /// </summary>
    public string PayloadJson { get; }

    /// <summary>
    /// Gets the checkpoint creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Initializes a serialized checkpoint record.
    /// </summary>
    public AgentCheckpointRecord(
        Guid checkpointId,
        Guid executionId,
        string graphId,
        long sequenceNumber,
        string payloadJson,
        DateTimeOffset createdAtUtc)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException(
                "Checkpoint ID cannot be empty.",
                nameof(checkpointId));
        }

        if (executionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution ID cannot be empty.",
                nameof(executionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            graphId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            payloadJson);

        if (sequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber),
                "Sequence number cannot be negative.");
        }

        if (createdAtUtc == default)
        {
            throw new ArgumentException(
                "Checkpoint creation time is required.",
                nameof(createdAtUtc));
        }

        CheckpointId = checkpointId;

        ExecutionId = executionId;

        GraphId = graphId.Trim();

        SequenceNumber = sequenceNumber;

        PayloadJson = payloadJson;

        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    /// <summary>
    /// Creates a checkpoint record using
    /// a generated checkpoint identifier.
    /// </summary>
    public static AgentCheckpointRecord Create(
        Guid executionId,
        string graphId,
        long sequenceNumber,
        string payloadJson)
    {
        return new AgentCheckpointRecord(
            Guid.NewGuid(),
            executionId,
            graphId,
            sequenceNumber,
            payloadJson,
            DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Represents metadata describing a
/// persisted agent graph checkpoint.
/// </summary>
/// <remarks>
/// A descriptor excludes the serialized
/// checkpoint payload.
///
/// This allows callers to enumerate
/// checkpoints without loading their
/// potentially large state data.
/// </remarks>
public sealed record AgentCheckpointDescriptor
{
    /// <summary>
    /// Gets the checkpoint identifier.
    /// </summary>
    public Guid CheckpointId { get; }

    /// <summary>
    /// Gets the execution identifier.
    /// </summary>
    public Guid ExecutionId { get; }

    /// <summary>
    /// Gets the graph identifier.
    /// </summary>
    public string GraphId { get; }

    /// <summary>
    /// Gets the checkpoint sequence number.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the checkpoint creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Initializes checkpoint metadata.
    /// </summary>
    public AgentCheckpointDescriptor(
        Guid checkpointId,
        Guid executionId,
        string graphId,
        long sequenceNumber,
        DateTimeOffset createdAtUtc)
    {
        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException(
                "Checkpoint ID cannot be empty.",
                nameof(checkpointId));
        }

        if (executionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution ID cannot be empty.",
                nameof(executionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            graphId);

        if (sequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber));
        }

        if (createdAtUtc == default)
        {
            throw new ArgumentException(
                "Checkpoint creation time is required.",
                nameof(createdAtUtc));
        }

        CheckpointId = checkpointId;

        ExecutionId = executionId;

        GraphId = graphId.Trim();

        SequenceNumber = sequenceNumber;

        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }

    /// <summary>
    /// Creates metadata from a checkpoint record.
    /// </summary>
    public static AgentCheckpointDescriptor FromRecord(
        AgentCheckpointRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new AgentCheckpointDescriptor(
            record.CheckpointId,
            record.ExecutionId,
            record.GraphId,
            record.SequenceNumber,
            record.CreatedAtUtc);
    }
}

/// <summary>
/// Defines the provider-neutral persistence
/// contract for agent graph checkpoints.
/// </summary>
/// <remarks>
/// Implementations must ensure that:
///
/// - Checkpoints are scoped to an execution.
/// - Checkpoint identifiers are unique.
/// - Sequence numbers are unique per execution.
/// - Latest checkpoint retrieval is deterministic.
/// - Writes are atomic where supported.
/// - Concurrent operations are handled safely.
/// - Cancellation is propagated appropriately.
///
/// Authorization, tenant isolation,
/// encryption, retention, and auditing
/// must be implemented by the surrounding
/// application and storage infrastructure.
///
/// This interface does not provide
/// transactional or security guarantees
/// by itself.
/// </remarks>
public interface IAgentCheckpointStore
{
    /// <summary>
    /// Persists a serialized graph checkpoint.
    /// </summary>
    /// <remarks>
    /// Implementations should reject conflicting
    /// checkpoint identifiers or sequence numbers
    /// rather than silently overwriting records.
    /// </remarks>
    Task SaveAsync(
        AgentCheckpointRecord checkpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific checkpoint
    /// within a graph execution.
    /// </summary>
    /// <returns>
    /// The checkpoint record, or null
    /// when no matching record exists.
    /// </returns>
    Task<AgentCheckpointRecord?> GetAsync(
        Guid executionId,
        Guid checkpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the latest checkpoint
    /// for a graph execution.
    /// </summary>
    /// <remarks>
    /// Latest is defined by the highest
    /// sequence number.
    ///
    /// Implementations must enforce unique
    /// sequence numbers per execution.
    /// </remarks>
    /// <returns>
    /// The latest checkpoint, or null
    /// when no checkpoint exists.
    /// </returns>
    Task<AgentCheckpointRecord?> GetLatestAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists checkpoint metadata
    /// for a graph execution.
    /// </summary>
    /// <remarks>
    /// Results must be ordered by descending
    /// sequence number.
    ///
    /// Implementations should return a stable
    /// snapshot of the metadata available
    /// at the time of the operation.
    /// </remarks>
    Task<IReadOnlyList<AgentCheckpointDescriptor>> ListAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a specific checkpoint
    /// within a graph execution.
    /// </summary>
    /// <returns>
    /// True when a checkpoint was deleted;
    /// otherwise false.
    /// </returns>
    Task<bool> DeleteAsync(
        Guid executionId,
        Guid checkpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all checkpoints associated
    /// with a graph execution.
    /// </summary>
    /// <returns>
    /// The number of deleted checkpoints.
    /// </returns>
    Task<int> DeleteExecutionAsync(
        Guid executionId,
        CancellationToken cancellationToken = default);
}