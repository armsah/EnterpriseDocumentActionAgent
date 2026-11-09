namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Defines the execution contract for
/// a transition between graph nodes.
/// </summary>
public interface IAgentGraphEdge
{
    /// <summary>
    /// Gets the source node identifier.
    /// </summary>
    string SourceNodeId { get; }

    /// <summary>
    /// Gets the destination node identifier.
    /// </summary>
    string TargetNodeId { get; }

    /// <summary>
    /// Gets the optional transition label.
    /// </summary>
    string? Label { get; }

    /// <summary>
    /// Gets whether the edge has an
    /// explicit transition condition.
    /// </summary>
    bool IsConditional { get; }

    /// <summary>
    /// Determines whether the edge is
    /// eligible for traversal.
    /// </summary>
    Task<bool> CanTraverseAsync(
        AgentGraphState state,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a directed transition between
/// two nodes in an agent execution graph.
/// </summary>
/// <remarks>
/// An edge may be unconditional or conditional.
///
/// Unconditional edges are always eligible
/// when their evaluation is not cancelled.
///
/// Conditional edges evaluate a predicate
/// against the current graph execution state.
///
/// The graph executor determines which
/// eligible edge to follow.
///
/// This component does not execute nodes,
/// mutate graph status, or persist checkpoints.
/// </remarks>
public sealed class AgentGraphEdge : IAgentGraphEdge
{
    private readonly Func<
        AgentGraphState,
        CancellationToken,
        Task<bool>>? _condition;

    /// <summary>
    /// Gets the source node identifier.
    /// </summary>
    public string SourceNodeId { get; }

    /// <summary>
    /// Gets the destination node identifier.
    /// </summary>
    public string TargetNodeId { get; }

    /// <summary>
    /// Gets the optional transition label.
    /// </summary>
    public string? Label { get; }

    /// <summary>
    /// Gets whether this edge has a condition.
    /// </summary>
    public bool IsConditional => _condition is not null;

    /// <summary>
    /// Initializes a directed graph edge.
    /// </summary>
    /// <param name="sourceNodeId">
    /// Identifier of the source graph node.
    /// </param>
    /// <param name="targetNodeId">
    /// Identifier of the destination graph node.
    /// </param>
    /// <param name="condition">
    /// Optional asynchronous transition condition.
    /// </param>
    /// <param name="label">
    /// Optional descriptive transition label.
    /// </param>
    public AgentGraphEdge(
        string sourceNodeId,
        string targetNodeId,
        Func<
            AgentGraphState,
            CancellationToken,
            Task<bool>>? condition = null,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceNodeId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            targetNodeId);

        SourceNodeId = sourceNodeId.Trim();

        TargetNodeId = targetNodeId.Trim();

        if (string.Equals(
            SourceNodeId,
            TargetNodeId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A graph edge cannot directly connect " +
                "a node to itself.",
                nameof(targetNodeId));
        }

        _condition = condition;

        Label = string.IsNullOrWhiteSpace(label)
            ? null
            : label.Trim();
    }

    /// <summary>
    /// Evaluates whether this edge can
    /// be traversed.
    /// </summary>
    /// <remarks>
    /// Unconditional edges return true
    /// unless cancellation is requested.
    ///
    /// Conditional edges invoke the
    /// configured asynchronous predicate.
    ///
    /// Exceptions and cancellation propagate
    /// to the graph executor.
    /// </remarks>
    public async Task<bool> CanTraverseAsync(
        AgentGraphState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        cancellationToken.ThrowIfCancellationRequested();

        if (_condition is null)
        {
            return true;
        }

        var task = _condition(
            state,
            cancellationToken);

        if (task is null)
        {
            throw new InvalidOperationException(
                $"Graph edge '{SourceNodeId}' -> " +
                $"'{TargetNodeId}' returned a null task.");
        }

        var result = await task.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        return result;
    }
}

/// <summary>
/// Provides factory methods for creating
/// directed agent graph transitions.
/// </summary>
public static class AgentGraphEdgeFactory
{
    /// <summary>
    /// Creates an unconditional graph edge.
    /// </summary>
    public static AgentGraphEdge Always(
        string sourceNodeId,
        string targetNodeId,
        string? label = null)
    {
        return new AgentGraphEdge(
            sourceNodeId,
            targetNodeId,
            condition: null,
            label: label);
    }

    /// <summary>
    /// Creates a conditional graph edge
    /// using an asynchronous predicate.
    /// </summary>
    public static AgentGraphEdge WhenAsync(
        string sourceNodeId,
        string targetNodeId,
        Func<
            AgentGraphState,
            CancellationToken,
            Task<bool>> condition,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return new AgentGraphEdge(
            sourceNodeId,
            targetNodeId,
            condition,
            label);
    }

    /// <summary>
    /// Creates a conditional graph edge
    /// using a synchronous predicate.
    /// </summary>
    /// <remarks>
    /// Synchronous predicates execute on
    /// the calling thread.
    ///
    /// This factory does not schedule
    /// synchronous work on the thread pool.
    /// </remarks>
    public static AgentGraphEdge When(
        string sourceNodeId,
        string targetNodeId,
        Func<AgentGraphState, bool> condition,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        return new AgentGraphEdge(
            sourceNodeId,
            targetNodeId,
            (state, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = condition(state);

                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(result);
            },
            label);
    }

    /// <summary>
    /// Creates a graph edge that is eligible
    /// when a typed state value equals
    /// an expected value.
    /// </summary>
    public static AgentGraphEdge WhenValueEquals<T>(
        string sourceNodeId,
        string targetNodeId,
        string stateKey,
        T expectedValue,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            stateKey);

        return When(
            sourceNodeId,
            targetNodeId,
            state =>
            {
                return state.TryGetValue<T>(
                    stateKey,
                    out var actualValue) &&
                    EqualityComparer<T>.Default.Equals(
                        actualValue!,
                        expectedValue);
            },
            label);
    }

    /// <summary>
    /// Creates a graph edge that is eligible
    /// when a named state key exists.
    /// </summary>
    public static AgentGraphEdge WhenKeyExists(
        string sourceNodeId,
        string targetNodeId,
        string stateKey,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            stateKey);

        return When(
            sourceNodeId,
            targetNodeId,
            state => state.ContainsKey(stateKey),
            label);
    }

    /// <summary>
    /// Creates a graph edge that is eligible
    /// when a named state key does not exist.
    /// </summary>
    public static AgentGraphEdge WhenKeyMissing(
        string sourceNodeId,
        string targetNodeId,
        string stateKey,
        string? label = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            stateKey);

        return When(
            sourceNodeId,
            targetNodeId,
            state => !state.ContainsKey(stateKey),
            label);
    }
}