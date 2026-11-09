namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Represents the result of an agent graph execution.
/// </summary>
/// <remarks>
/// The result contains a point-in-time snapshot
/// of graph execution state.
///
/// State values are shallow-copied by the
/// underlying snapshot implementation.
/// </remarks>
public sealed record AgentGraphExecutionResult
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
    /// Gets the final execution status.
    /// </summary>
    public AgentGraphExecutionStatus Status { get; }

    /// <summary>
    /// Gets the last active node identifier.
    /// </summary>
    public string? LastNodeId { get; }

    /// <summary>
    /// Gets the number of completed steps.
    /// </summary>
    public int CompletedSteps { get; }

    /// <summary>
    /// Gets an optional execution message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the final execution-state snapshot.
    /// </summary>
    public AgentGraphStateSnapshot StateSnapshot { get; }

    /// <summary>
    /// Gets whether execution completed successfully.
    /// </summary>
    public bool IsCompleted =>
        Status == AgentGraphExecutionStatus.Completed;

    /// <summary>
    /// Gets whether execution is paused.
    /// </summary>
    public bool IsPaused =>
        Status == AgentGraphExecutionStatus.Paused;

    /// <summary>
    /// Gets whether execution failed.
    /// </summary>
    public bool IsFailed =>
        Status == AgentGraphExecutionStatus.Failed;

    /// <summary>
    /// Initializes an execution result.
    /// </summary>
    public AgentGraphExecutionResult(
        string graphId,
        AgentGraphStateSnapshot stateSnapshot,
        string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            graphId);

        ArgumentNullException.ThrowIfNull(
            stateSnapshot);

        GraphId = graphId.Trim();

        ExecutionId = stateSnapshot.ExecutionId;

        Status = stateSnapshot.Status;

        LastNodeId = stateSnapshot.CurrentNodeId;

        CompletedSteps = stateSnapshot.CompletedSteps;

        Message = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();

        StateSnapshot = stateSnapshot;
    }
}

/// <summary>
/// Executes validated agent graphs using
/// their registered nodes and transitions.
/// </summary>
/// <remarks>
/// Nodes execute sequentially.
///
/// Outgoing edges are evaluated in their
/// registration order.
///
/// The first eligible edge is selected.
///
/// If no outgoing edge is eligible,
/// execution completes successfully.
///
/// Node failures are represented by a
/// failed graph execution result.
///
/// Unexpected exceptions propagate after
/// the graph state is marked as failed.
///
/// Cancellation propagates to the caller.
///
/// The executor does not persist checkpoints,
/// perform retries, or enforce authorization.
/// </remarks>
public sealed class AgentGraphExecutor
{
    /// <summary>
    /// Executes a graph using the supplied state.
    /// </summary>
    public async Task<AgentGraphExecutionResult> ExecuteAsync(
        AgentGraphDefinition graph,
        AgentGraphState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(state);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateInitialState(graph, state);

        var currentNodeId = ResolveStartingNode(
            graph,
            state);

        try
        {
            state.MarkRunning();

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (state.CompletedSteps >=
                    state.MaxExecutionSteps)
                {
                    state.MarkFailed();

                    return CreateResult(
                        graph,
                        state,
                        "Graph execution step limit reached.");
                }

                var node = graph.GetNode(currentNodeId);

                state.SetCurrentNode(currentNodeId);

                var nodeResult = await node.ExecuteAsync(
                    state,
                    cancellationToken).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                if (nodeResult.IsFailure)
                {
                    state.MarkFailed();

                    return CreateResult(
                        graph,
                        state,
                        nodeResult.Message ??
                        $"Graph node '{currentNodeId}' failed.");
                }

                if (nodeResult.IsPaused)
                {
                    state.MarkPaused();

                    return CreateResult(
                        graph,
                        state,
                        nodeResult.Message ??
                        $"Graph paused at node '{currentNodeId}'.");
                }

                state.CompleteStep();

                var outgoingEdges =
                    graph.GetOutgoingEdges(currentNodeId);

                if (outgoingEdges.Count == 0)
                {
                    state.MarkCompleted();

                    return CreateResult(
                        graph,
                        state,
                        $"Graph completed at terminal node " +
                        $"'{currentNodeId}'.");
                }

                string? nextNodeId = null;

                foreach (var edge in outgoingEdges)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await edge.CanTraverseAsync(
                        state,
                        cancellationToken).ConfigureAwait(false))
                    {
                        nextNodeId = edge.TargetNodeId;

                        break;
                    }
                }

                if (nextNodeId is null)
                {
                    state.MarkCompleted();

                    return CreateResult(
                        graph,
                        state,
                        $"No eligible outgoing transition " +
                        $"from node '{currentNodeId}'.");
                }

                currentNodeId = nextNodeId;
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            if (state.Status == AgentGraphExecutionStatus.Running)
            {
                state.MarkPaused();
            }

            throw;
        }
        catch
        {
            if (state.Status is not
                (AgentGraphExecutionStatus.Completed
                or AgentGraphExecutionStatus.Failed))
            {
                state.MarkFailed();
            }

            throw;
        }
    }

    /// <summary>
    /// Executes a graph with a newly created
    /// execution state.
    /// </summary>
    public Task<AgentGraphExecutionResult> ExecuteAsync(
        AgentGraphDefinition graph,
        string agentId,
        Guid? conversationId = null,
        int maxExecutionSteps = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var state = new AgentGraphState(
            agentId: agentId,
            conversationId: conversationId,
            maxExecutionSteps: maxExecutionSteps);

        return ExecuteAsync(
            graph,
            state,
            cancellationToken);
    }

    /// <summary>
    /// Validates that the supplied state can
    /// begin or resume graph execution.
    /// </summary>
    private static void ValidateInitialState(
        AgentGraphDefinition graph,
        AgentGraphState state)
    {
        if (state.Status is
            AgentGraphExecutionStatus.Completed
            or AgentGraphExecutionStatus.Failed)
        {
            throw new InvalidOperationException(
                "A completed or failed graph execution " +
                "cannot be started again.");
        }

        if (state.Status ==
            AgentGraphExecutionStatus.Running)
        {
            throw new InvalidOperationException(
                "The graph execution is already running.");
        }

        if (state.CurrentNodeId is { } currentNodeId &&
            !graph.ContainsNode(currentNodeId))
        {
            throw new InvalidOperationException(
                $"Current node '{currentNodeId}' " +
                $"does not exist in graph '{graph.GraphId}'.");
        }
    }

    /// <summary>
    /// Resolves the entry point for a new
    /// or paused graph execution.
    /// </summary>
    private static string ResolveStartingNode(
        AgentGraphDefinition graph,
        AgentGraphState state)
    {
        if (state.Status ==
            AgentGraphExecutionStatus.Paused &&
            state.CurrentNodeId is { } currentNodeId)
        {
            return currentNodeId;
        }

        return graph.EntryNodeId;
    }

    /// <summary>
    /// Creates an execution result from
    /// the current graph state.
    /// </summary>
    private static AgentGraphExecutionResult CreateResult(
        AgentGraphDefinition graph,
        AgentGraphState state,
        string? message)
    {
        return new AgentGraphExecutionResult(
            graph.GraphId,
            state.CreateSnapshot(),
            message);
    }
}