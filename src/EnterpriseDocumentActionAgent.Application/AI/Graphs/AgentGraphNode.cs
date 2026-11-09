namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Represents the outcome of executing
/// an agent graph node.
/// </summary>
public enum AgentGraphNodeOutcome
{
    Succeeded = 0,
    Paused = 1,
    Failed = 2
}

/// <summary>
/// Represents the result of executing
/// an individual agent graph node.
/// </summary>
/// <remarks>
/// A node result describes the outcome
/// of one node invocation.
///
/// It does not automatically change the
/// execution status of AgentGraphState.
/// </remarks>
public sealed record AgentGraphNodeResult
{
    /// <summary>
    /// Gets the node execution outcome.
    /// </summary>
    public AgentGraphNodeOutcome Outcome { get; }

    /// <summary>
    /// Gets an optional result message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets whether the node succeeded.
    /// </summary>
    public bool IsSuccess =>
        Outcome == AgentGraphNodeOutcome.Succeeded;

    /// <summary>
    /// Gets whether the node requested a pause.
    /// </summary>
    public bool IsPaused =>
        Outcome == AgentGraphNodeOutcome.Paused;

    /// <summary>
    /// Gets whether the node failed.
    /// </summary>
    public bool IsFailure =>
        Outcome == AgentGraphNodeOutcome.Failed;

    /// <summary>
    /// Initializes a node execution result.
    /// </summary>
    public AgentGraphNodeResult(
        AgentGraphNodeOutcome outcome,
        string? message = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                "Invalid graph node outcome.");
        }

        Outcome = outcome;

        Message = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();
    }

    /// <summary>
    /// Creates a successful node result.
    /// </summary>
    public static AgentGraphNodeResult Success(
        string? message = null)
    {
        return new AgentGraphNodeResult(
            AgentGraphNodeOutcome.Succeeded,
            message);
    }

    /// <summary>
    /// Creates a paused node result.
    /// </summary>
    public static AgentGraphNodeResult Pause(
        string? message = null)
    {
        return new AgentGraphNodeResult(
            AgentGraphNodeOutcome.Paused,
            message);
    }

    /// <summary>
    /// Creates a failed node result.
    /// </summary>
    public static AgentGraphNodeResult Failure(
        string? message = null)
    {
        return new AgentGraphNodeResult(
            AgentGraphNodeOutcome.Failed,
            message);
    }
}

/// <summary>
/// Defines the execution contract for
/// an agent graph node.
/// </summary>
public interface IAgentGraphNode
{
    /// <summary>
    /// Gets the unique node identifier.
    /// </summary>
    string NodeId { get; }

    /// <summary>
    /// Gets the descriptive node name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the node against the
    /// supplied graph execution state.
    /// </summary>
    Task<AgentGraphNodeResult> ExecuteAsync(
        AgentGraphState state,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an executable node within
/// an agent orchestration graph.
/// </summary>
/// <remarks>
/// A node wraps an asynchronous handler
/// that receives the shared graph state.
///
/// Nodes do not automatically manage graph
/// transitions, execution step counters,
/// checkpoint persistence, or authorization.
///
/// Those responsibilities belong to the
/// graph executor and surrounding services.
/// </remarks>
public sealed class AgentGraphNode : IAgentGraphNode
{
    private readonly Func<
        AgentGraphState,
        CancellationToken,
        Task<AgentGraphNodeResult>> _handler;

    /// <summary>
    /// Gets the unique node identifier.
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// Gets the descriptive node name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Initializes an executable graph node.
    /// </summary>
    public AgentGraphNode(
        string nodeId,
        string name,
        Func<
            AgentGraphState,
            CancellationToken,
            Task<AgentGraphNodeResult>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        ArgumentNullException.ThrowIfNull(handler);

        NodeId = nodeId.Trim();

        Name = name.Trim();

        _handler = handler;
    }

    /// <summary>
    /// Executes the configured node handler.
    /// </summary>
    /// <remarks>
    /// Exceptions from the handler propagate
    /// to the caller.
    ///
    /// Cancellation is not converted into a
    /// failure result.
    ///
    /// The graph executor is responsible for
    /// deciding how to handle these conditions.
    /// </remarks>
    public async Task<AgentGraphNodeResult> ExecuteAsync(
        AgentGraphState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        cancellationToken.ThrowIfCancellationRequested();

        var task = _handler(
            state,
            cancellationToken);

        if (task is null)
        {
            throw new InvalidOperationException(
                $"Graph node '{NodeId}' returned a null task.");
        }

        var result = await task.ConfigureAwait(false);

        if (result is null)
        {
            throw new InvalidOperationException(
                $"Graph node '{NodeId}' returned a null result.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        return result;
    }
}

/// <summary>
/// Provides factory methods for creating
/// executable agent graph nodes.
/// </summary>
public static class AgentGraphNodeFactory
{
    /// <summary>
    /// Creates a node from an asynchronous
    /// execution handler.
    /// </summary>
    public static AgentGraphNode FromAsync(
        string nodeId,
        string name,
        Func<
            AgentGraphState,
            CancellationToken,
            Task<AgentGraphNodeResult>> handler)
    {
        return new AgentGraphNode(
            nodeId,
            name,
            handler);
    }

    /// <summary>
    /// Creates a node from a synchronous
    /// execution handler.
    /// </summary>
    /// <remarks>
    /// Synchronous handlers execute on the
    /// calling thread.
    ///
    /// This factory does not schedule work
    /// on the thread pool.
    /// </remarks>
    public static AgentGraphNode FromSync(
        string nodeId,
        string name,
        Func<AgentGraphState, AgentGraphNodeResult> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return new AgentGraphNode(
            nodeId,
            name,
            (state, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = handler(state);

                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(result);
            });
    }

    /// <summary>
    /// Creates a node that always succeeds
    /// without modifying graph state.
    /// </summary>
    public static AgentGraphNode NoOp(
        string nodeId,
        string name)
    {
        return new AgentGraphNode(
            nodeId,
            name,
            (state, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(
                    AgentGraphNodeResult.Success());
            });
    }

    /// <summary>
    /// Creates a node that requests a pause.
    /// </summary>
    public static AgentGraphNode Pause(
        string nodeId,
        string name,
        string? message = null)
    {
        return new AgentGraphNode(
            nodeId,
            name,
            (state, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(
                    AgentGraphNodeResult.Pause(message));
            });
    }

    /// <summary>
    /// Creates a node that reports failure.
    /// </summary>
    public static AgentGraphNode Fail(
        string nodeId,
        string name,
        string? message = null)
    {
        return new AgentGraphNode(
            nodeId,
            name,
            (state, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(
                    AgentGraphNodeResult.Failure(message));
            });
    }
}