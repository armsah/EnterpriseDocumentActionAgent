namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Represents the result of preparing an
/// agent graph execution for recovery.
/// </summary>
/// <remarks>
/// This result contains the original checkpoint
/// and a newly constructed state instance.
///
/// The state instance contains restored
/// execution identity and JSON state values.
///
/// It does not contain a fully restored
/// execution lifecycle.
///
/// In particular, the restored state remains
/// in the Created status, with zero completed
/// steps and no restored current-node cursor.
///
/// Callers must not treat this result as
/// authorization to restart execution.
/// </remarks>
public sealed record AgentGraphRecoveryResult
{
    /// <summary>
    /// Gets the graph identifier.
    /// </summary>
    public string GraphId { get; }

    /// <summary>
    /// Gets the execution identifier.
    /// </summary>
    public Guid ExecutionId { get; }

    /// <summary>
    /// Gets the recovered checkpoint.
    /// </summary>
    public AgentGraphCheckpoint Checkpoint { get; }

    /// <summary>
    /// Gets the partially restored state.
    /// </summary>
    public AgentGraphState RestoredState { get; }

    /// <summary>
    /// Gets the status recorded in the checkpoint.
    /// </summary>
    public AgentGraphExecutionStatus
        CheckpointStatus => Checkpoint.Status;

    /// <summary>
    /// Gets the node recorded in the checkpoint.
    /// </summary>
    public string? CheckpointNodeId =>
        Checkpoint.CurrentNodeId;

    /// <summary>
    /// Gets the completed-step count recorded
    /// in the checkpoint.
    /// </summary>
    public int CheckpointCompletedSteps =>
        Checkpoint.CompletedSteps;

    /// <summary>
    /// Gets whether the checkpoint represents
    /// a paused graph execution.
    /// </summary>
    public bool WasPaused =>
        Checkpoint.Status ==
        AgentGraphExecutionStatus.Paused;

    /// <summary>
    /// Gets whether the checkpoint represents
    /// a completed graph execution.
    /// </summary>
    public bool WasCompleted =>
        Checkpoint.Status ==
        AgentGraphExecutionStatus.Completed;

    /// <summary>
    /// Gets whether the checkpoint represents
    /// a failed graph execution.
    /// </summary>
    public bool WasFailed =>
        Checkpoint.Status ==
        AgentGraphExecutionStatus.Failed;

    /// <summary>
    /// Gets whether full execution lifecycle
    /// restoration has been performed.
    /// </summary>
    /// <remarks>
    /// Always false for the current implementation.
    /// </remarks>
    public bool IsFullyRestored => false;

    /// <summary>
    /// Initializes a recovery result.
    /// </summary>
    public AgentGraphRecoveryResult(
        string graphId,
        AgentGraphCheckpoint checkpoint,
        AgentGraphState restoredState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            graphId);

        ArgumentNullException.ThrowIfNull(
            checkpoint);

        ArgumentNullException.ThrowIfNull(
            restoredState);

        if (!string.Equals(
            graphId,
            checkpoint.GraphId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Recovery graph ID does not match " +
                "the checkpoint graph ID.",
                nameof(graphId));
        }

        if (restoredState.ExecutionId !=
            checkpoint.ExecutionId)
        {
            throw new ArgumentException(
                "Restored state execution ID does not " +
                "match the checkpoint execution ID.",
                nameof(restoredState));
        }

        if (!string.Equals(
            restoredState.AgentId,
            checkpoint.AgentId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Restored state agent ID does not " +
                "match the checkpoint agent ID.",
                nameof(restoredState));
        }

        if (restoredState.ConversationId !=
            checkpoint.ConversationId)
        {
            throw new ArgumentException(
                "Restored state conversation ID does not " +
                "match the checkpoint conversation ID.",
                nameof(restoredState));
        }

        if (restoredState.MaxExecutionSteps !=
            checkpoint.MaxExecutionSteps)
        {
            throw new ArgumentException(
                "Restored state execution limit does not " +
                "match the checkpoint execution limit.",
                nameof(restoredState));
        }

        GraphId = graphId.Trim();

        ExecutionId = checkpoint.ExecutionId;

        Checkpoint = checkpoint;

        RestoredState = restoredState;
    }
}

/// <summary>
/// Coordinates retrieval, validation, and
/// preparation of agent graph checkpoints.
/// </summary>
/// <remarks>
/// The service loads checkpoints through the
/// provider-neutral IAgentCheckpointStore.
///
/// It validates checkpoint metadata and graph
/// compatibility before reconstructing state data.
///
/// This service does not execute graph nodes.
///
/// Full durable execution resumption requires
/// additional lifecycle restoration support
/// in AgentGraphState and AgentGraphExecutor.
///
/// The service does not enforce authorization,
/// tenant isolation, or checkpoint ownership.
/// Those controls must be applied by callers
/// and storage implementations.
/// </remarks>
public sealed class AgentGraphResumeService
{
    private readonly IAgentCheckpointStore
        _checkpointStore;

    /// <summary>
    /// Initializes the resume preparation service.
    /// </summary>
    public AgentGraphResumeService(
        IAgentCheckpointStore checkpointStore)
    {
        ArgumentNullException.ThrowIfNull(
            checkpointStore);

        _checkpointStore = checkpointStore;
    }

    /// <summary>
    /// Loads the latest checkpoint for an
    /// execution and prepares its state data.
    /// </summary>
    /// <returns>
    /// A recovery result, or null when no
    /// checkpoint exists for the execution.
    /// </returns>
    public async Task<AgentGraphRecoveryResult?>
        PrepareLatestAsync(
            AgentGraphDefinition graph,
            Guid executionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        ValidateExecutionId(executionId);

        cancellationToken.ThrowIfCancellationRequested();

        var record = await _checkpointStore
            .GetLatestAsync(
                executionId,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (record is null)
        {
            return null;
        }

        return PrepareFromRecord(
            graph,
            executionId,
            record);
    }

    /// <summary>
    /// Loads a specific checkpoint and
    /// prepares its state data.
    /// </summary>
    /// <returns>
    /// A recovery result, or null when the
    /// requested checkpoint does not exist.
    /// </returns>
    public async Task<AgentGraphRecoveryResult?>
        PrepareAsync(
            AgentGraphDefinition graph,
            Guid executionId,
            Guid checkpointId,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        ValidateExecutionId(executionId);

        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException(
                "Checkpoint ID cannot be empty.",
                nameof(checkpointId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var record = await _checkpointStore
            .GetAsync(
                executionId,
                checkpointId,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (record is null)
        {
            return null;
        }

        return PrepareFromRecord(
            graph,
            executionId,
            record);
    }

    /// <summary>
    /// Prepares recovery directly from a
    /// previously retrieved checkpoint record.
    /// </summary>
    /// <remarks>
    /// This method does not access the
    /// checkpoint store.
    ///
    /// The record is assumed to have been
    /// retrieved through an authorized path.
    /// </remarks>
    public AgentGraphRecoveryResult PrepareFromRecord(
        AgentGraphDefinition graph,
        Guid executionId,
        AgentCheckpointRecord record)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(record);

        ValidateExecutionId(executionId);

        if (record.ExecutionId != executionId)
        {
            throw new InvalidOperationException(
                "Checkpoint record belongs to " +
                "a different graph execution.");
        }

        if (!string.Equals(
            record.GraphId,
            graph.GraphId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Checkpoint graph '{record.GraphId}' " +
                $"does not match requested graph " +
                $"'{graph.GraphId}'.");
        }

        var checkpoint =
            AgentGraphCheckpointSerializer.FromRecord(
                record);

        ValidateCheckpoint(
            graph,
            executionId,
            checkpoint);

        var restoredState =
            AgentGraphCheckpointFactory.RestoreStateData(
                checkpoint);

        return new AgentGraphRecoveryResult(
            graph.GraphId,
            checkpoint,
            restoredState);
    }

    /// <summary>
    /// Validates whether a checkpoint is
    /// structurally compatible with a graph.
    /// </summary>
    /// <remarks>
    /// This validation checks graph identity
    /// and recorded node existence.
    ///
    /// It does not prove that the current graph
    /// has the same topology or node behavior
    /// as the graph used when the checkpoint
    /// was originally created.
    /// </remarks>
    public static void ValidateCheckpoint(
        AgentGraphDefinition graph,
        Guid executionId,
        AgentGraphCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(graph);

        ArgumentNullException.ThrowIfNull(checkpoint);

        ValidateExecutionId(executionId);

        if (checkpoint.ExecutionId != executionId)
        {
            throw new InvalidOperationException(
                "Checkpoint execution ID does not " +
                "match the requested execution.");
        }

        if (!string.Equals(
            checkpoint.GraphId,
            graph.GraphId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Checkpoint graph ID does not " +
                "match the requested graph.");
        }

        if (checkpoint.CurrentNodeId is { } nodeId &&
            !graph.ContainsNode(nodeId))
        {
            throw new InvalidOperationException(
                $"Checkpoint references node '{nodeId}', " +
                $"which does not exist in graph " +
                $"'{graph.GraphId}'.");
        }

        if (checkpoint.Status ==
            AgentGraphExecutionStatus.Paused &&
            checkpoint.CurrentNodeId is null)
        {
            throw new InvalidOperationException(
                "A paused checkpoint must contain " +
                "a current node identifier.");
        }

        if (checkpoint.Status ==
            AgentGraphExecutionStatus.Running &&
            checkpoint.CurrentNodeId is null)
        {
            throw new InvalidOperationException(
                "A running checkpoint must contain " +
                "a current node identifier.");
        }
    }

    /// <summary>
    /// Validates a graph execution identifier.
    /// </summary>
    private static void ValidateExecutionId(
        Guid executionId)
    {
        if (executionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution ID cannot be empty.",
                nameof(executionId));
        }
    }
}