using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Represents a versioned, serializable checkpoint
/// of an agent graph execution.
/// </summary>
/// <remarks>
/// Checkpoints preserve graph execution metadata
/// and JSON-compatible state values.
///
/// State values are represented as JsonElement
/// instances to avoid unsafe polymorphic
/// deserialization of arbitrary CLR types.
///
/// A checkpoint does not itself persist data
/// or resume graph execution.
/// </remarks>
public sealed class AgentGraphCheckpoint
{
    /// <summary>
    /// Gets the current checkpoint schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// Gets the checkpoint schema version.
    /// </summary>
    public int SchemaVersion { get; }

    /// <summary>
    /// Gets the checkpoint identifier.
    /// </summary>
    public Guid CheckpointId { get; }

    /// <summary>
    /// Gets the graph execution identifier.
    /// </summary>
    public Guid ExecutionId { get; }

    /// <summary>
    /// Gets the graph identifier.
    /// </summary>
    public string GraphId { get; }

    /// <summary>
    /// Gets the agent identifier.
    /// </summary>
    public string AgentId { get; }

    /// <summary>
    /// Gets the optional conversation identifier.
    /// </summary>
    public Guid? ConversationId { get; }

    /// <summary>
    /// Gets the currently recorded node identifier.
    /// </summary>
    public string? CurrentNodeId { get; }

    /// <summary>
    /// Gets the recorded graph execution status.
    /// </summary>
    public AgentGraphExecutionStatus Status { get; }

    /// <summary>
    /// Gets the number of completed execution steps.
    /// </summary>
    public int CompletedSteps { get; }

    /// <summary>
    /// Gets the maximum permitted execution steps.
    /// </summary>
    public int MaxExecutionSteps { get; }

    /// <summary>
    /// Gets the original execution creation time.
    /// </summary>
    public DateTimeOffset ExecutionCreatedAtUtc { get; }

    /// <summary>
    /// Gets the most recent execution update time.
    /// </summary>
    public DateTimeOffset ExecutionUpdatedAtUtc { get; }

    /// <summary>
    /// Gets the checkpoint sequence number.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the checkpoint creation time.
    /// </summary>
    public DateTimeOffset CheckpointCreatedAtUtc { get; }

    /// <summary>
    /// Gets the serialized graph-state values.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Values { get; }

    /// <summary>
    /// Initializes a graph execution checkpoint.
    /// </summary>
    [JsonConstructor]
    public AgentGraphCheckpoint(
        int schemaVersion,
        Guid checkpointId,
        Guid executionId,
        string graphId,
        string agentId,
        Guid? conversationId,
        string? currentNodeId,
        AgentGraphExecutionStatus status,
        int completedSteps,
        int maxExecutionSteps,
        DateTimeOffset executionCreatedAtUtc,
        DateTimeOffset executionUpdatedAtUtc,
        long sequenceNumber,
        DateTimeOffset checkpointCreatedAtUtc,
        Dictionary<string, JsonElement> values)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                $"Unsupported checkpoint schema version: {schemaVersion}.");
        }

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

        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(values);

        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Conversation ID cannot be empty.",
                nameof(conversationId));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status));
        }

        if (completedSteps < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedSteps));
        }

        if (maxExecutionSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExecutionSteps));
        }

        if (completedSteps > maxExecutionSteps)
        {
            throw new ArgumentException(
                "Completed steps cannot exceed the execution limit.",
                nameof(completedSteps));
        }

        if (sequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber));
        }

        if (executionCreatedAtUtc == default)
        {
            throw new ArgumentException(
                "Execution creation time is required.",
                nameof(executionCreatedAtUtc));
        }

        if (executionUpdatedAtUtc == default)
        {
            throw new ArgumentException(
                "Execution update time is required.",
                nameof(executionUpdatedAtUtc));
        }

        if (checkpointCreatedAtUtc == default)
        {
            throw new ArgumentException(
                "Checkpoint creation time is required.",
                nameof(checkpointCreatedAtUtc));
        }

        if (executionUpdatedAtUtc < executionCreatedAtUtc)
        {
            throw new ArgumentException(
                "Execution update time cannot precede creation time.",
                nameof(executionUpdatedAtUtc));
        }

        if (checkpointCreatedAtUtc < executionCreatedAtUtc)
        {
            throw new ArgumentException(
                "Checkpoint time cannot precede execution creation time.",
                nameof(checkpointCreatedAtUtc));
        }

        if (currentNodeId is not null &&
            string.IsNullOrWhiteSpace(currentNodeId))
        {
            throw new ArgumentException(
                "Current node ID cannot be whitespace.",
                nameof(currentNodeId));
        }

        var copiedValues = new Dictionary<string, JsonElement>(
            StringComparer.Ordinal);

        foreach (var pair in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);

            if (pair.Value.ValueKind == JsonValueKind.Undefined)
            {
                throw new ArgumentException(
                    $"State value '{pair.Key}' is undefined.",
                    nameof(values));
            }

            copiedValues.Add(
                pair.Key,
                pair.Value.Clone());
        }

        SchemaVersion = schemaVersion;
        CheckpointId = checkpointId;
        ExecutionId = executionId;
        GraphId = graphId.Trim();
        AgentId = agentId.Trim();
        ConversationId = conversationId;
        CurrentNodeId = currentNodeId?.Trim();
        Status = status;
        CompletedSteps = completedSteps;
        MaxExecutionSteps = maxExecutionSteps;

        ExecutionCreatedAtUtc =
            executionCreatedAtUtc.ToUniversalTime();

        ExecutionUpdatedAtUtc =
            executionUpdatedAtUtc.ToUniversalTime();

        SequenceNumber = sequenceNumber;

        CheckpointCreatedAtUtc =
            checkpointCreatedAtUtc.ToUniversalTime();

        Values = new ReadOnlyDictionary<string, JsonElement>(
            copiedValues);
    }

    /// <summary>
    /// Creates a storage record containing
    /// this checkpoint's serialized payload.
    /// </summary>
    public AgentCheckpointRecord ToRecord()
    {
        return new AgentCheckpointRecord(
            CheckpointId,
            ExecutionId,
            GraphId,
            SequenceNumber,
            AgentGraphCheckpointSerializer.Serialize(this),
            CheckpointCreatedAtUtc);
    }
}

/// <summary>
/// Serializes and deserializes graph checkpoints.
/// </summary>
public static class AgentGraphCheckpointSerializer
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = false,
            MaxDepth = 64
        };

    /// <summary>
    /// Serializes a graph checkpoint to JSON.
    /// </summary>
    public static string Serialize(
        AgentGraphCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        return JsonSerializer.Serialize(
            checkpoint,
            SerializerOptions);
    }

    /// <summary>
    /// Deserializes a checkpoint from JSON.
    /// </summary>
    public static AgentGraphCheckpoint Deserialize(
        string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            payloadJson);

        var checkpoint =
            JsonSerializer.Deserialize<AgentGraphCheckpoint>(
                payloadJson,
                SerializerOptions);

        return checkpoint ??
            throw new JsonException(
                "Checkpoint payload deserialized to null.");
    }

    /// <summary>
    /// Deserializes and validates a stored
    /// checkpoint record.
    /// </summary>
    public static AgentGraphCheckpoint FromRecord(
        AgentCheckpointRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var checkpoint = Deserialize(record.PayloadJson);

        if (checkpoint.CheckpointId != record.CheckpointId)
        {
            throw new InvalidOperationException(
                "Checkpoint ID does not match the storage record.");
        }

        if (checkpoint.ExecutionId != record.ExecutionId)
        {
            throw new InvalidOperationException(
                "Execution ID does not match the storage record.");
        }

        if (!string.Equals(
            checkpoint.GraphId,
            record.GraphId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Graph ID does not match the storage record.");
        }

        if (checkpoint.SequenceNumber != record.SequenceNumber)
        {
            throw new InvalidOperationException(
                "Checkpoint sequence number does not match the storage record.");
        }

        if (checkpoint.CheckpointCreatedAtUtc != record.CreatedAtUtc)
        {
            throw new InvalidOperationException(
                "Checkpoint creation time does not match the storage record.");
        }

        return checkpoint;
    }
}

/// <summary>
/// Creates graph checkpoints from execution
/// snapshots and restores checkpoint state data.
/// </summary>
public static class AgentGraphCheckpointFactory
{
    /// <summary>
    /// Creates a checkpoint from a graph-state snapshot.
    /// </summary>
    public static AgentGraphCheckpoint Create(
        string graphId,
        AgentGraphStateSnapshot snapshot,
        long sequenceNumber,
        Guid? checkpointId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException(
                "Checkpoint ID cannot be empty.",
                nameof(checkpointId));
        }

        if (sequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber));
        }

        var serializedValues =
            new Dictionary<string, JsonElement>(
                StringComparer.Ordinal);

        foreach (var pair in snapshot.Values)
        {
            try
            {
                serializedValues.Add(
                    pair.Key,
                    JsonSerializer.SerializeToElement(
                        pair.Value,
                        pair.Value?.GetType() ?? typeof(object)));
            }
            catch (Exception exception)
                when (exception is JsonException
                    or NotSupportedException)
            {
                throw new InvalidOperationException(
                    $"Graph state value '{pair.Key}' " +
                    "cannot be serialized to JSON.",
                    exception);
            }
        }

        return new AgentGraphCheckpoint(
            AgentGraphCheckpoint.CurrentSchemaVersion,
            checkpointId ?? Guid.NewGuid(),
            snapshot.ExecutionId,
            graphId,
            snapshot.AgentId,
            snapshot.ConversationId,
            snapshot.CurrentNodeId,
            snapshot.Status,
            snapshot.CompletedSteps,
            snapshot.MaxExecutionSteps,
            snapshot.CreatedAtUtc,
            snapshot.UpdatedAtUtc,
            sequenceNumber,
            DateTimeOffset.UtcNow,
            serializedValues);
    }

    /// <summary>
    /// Creates a new graph-state instance containing
    /// the checkpoint's execution identity and values.
    /// </summary>
    /// <remarks>
    /// This method performs partial restoration only.
    ///
    /// The existing AgentGraphState constructor always
    /// initializes lifecycle status to Created and
    /// completed steps to zero.
    ///
    /// Consequently, this method does not restore:
    ///
    /// - Original lifecycle status.
    /// - Completed execution-step count.
    /// - Original creation/update timestamps.
    /// - Current node execution cursor.
    ///
    /// The returned state is not a fully resumed
    /// graph execution.
    ///
    /// A future restoration mechanism must address
    /// these limitations before durable resume is
    /// considered operational.
    /// </remarks>
    public static AgentGraphState RestoreStateData(
        AgentGraphCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var restoredValues =
            new Dictionary<string, object?>(
                StringComparer.Ordinal);

        foreach (var pair in checkpoint.Values)
        {
            restoredValues.Add(
                pair.Key,
                pair.Value.Clone());
        }

        return new AgentGraphState(
            agentId: checkpoint.AgentId,
            conversationId: checkpoint.ConversationId,
            executionId: checkpoint.ExecutionId,
            maxExecutionSteps: checkpoint.MaxExecutionSteps,
            initialValues: restoredValues);
    }
}