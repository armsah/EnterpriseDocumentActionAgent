using System.Collections.ObjectModel;

namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Represents a validated definition of
/// an executable agent graph.
/// </summary>
/// <remarks>
/// A graph definition contains registered
/// nodes, directed edges, and an entry node.
///
/// The definition does not execute nodes
/// or manage execution state.
///
/// Collections are copied during construction
/// and exposed through read-only wrappers.
///
/// Node and edge instances themselves are
/// not cloned.
/// </remarks>
public sealed class AgentGraphDefinition
{
    private readonly IReadOnlyDictionary<
        string,
        IAgentGraphNode> _nodes;

    private readonly IReadOnlyList<
        IAgentGraphEdge> _edges;

    private readonly IReadOnlyDictionary<
        string,
        IReadOnlyList<IAgentGraphEdge>> _outgoingEdges;

    /// <summary>
    /// Gets the unique graph identifier.
    /// </summary>
    public string GraphId { get; }

    /// <summary>
    /// Gets the graph entry-node identifier.
    /// </summary>
    public string EntryNodeId { get; }

    /// <summary>
    /// Gets the registered graph nodes.
    /// </summary>
    public IReadOnlyDictionary<
        string,
        IAgentGraphNode> Nodes => _nodes;

    /// <summary>
    /// Gets the registered graph edges.
    /// </summary>
    public IReadOnlyList<
        IAgentGraphEdge> Edges => _edges;

    /// <summary>
    /// Initializes a validated graph definition.
    /// </summary>
    internal AgentGraphDefinition(
        string graphId,
        string entryNodeId,
        IReadOnlyDictionary<
            string,
            IAgentGraphNode> nodes,
        IReadOnlyList<
            IAgentGraphEdge> edges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(graphId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            entryNodeId);

        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        GraphId = graphId.Trim();

        EntryNodeId = entryNodeId.Trim();

        var nodeCopy = new Dictionary<
            string,
            IAgentGraphNode>(
                nodes,
                StringComparer.Ordinal);

        var edgeCopy = edges.ToArray();

        _nodes = new ReadOnlyDictionary<
            string,
            IAgentGraphNode>(nodeCopy);

        _edges = Array.AsReadOnly(edgeCopy);

        var outgoing = new Dictionary<
            string,
            IReadOnlyList<IAgentGraphEdge>>(
                StringComparer.Ordinal);

        foreach (var nodeId in nodeCopy.Keys)
        {
            var matchingEdges = edgeCopy
                .Where(edge => string.Equals(
                    edge.SourceNodeId,
                    nodeId,
                    StringComparison.Ordinal))
                .ToArray();

            outgoing[nodeId] =
                Array.AsReadOnly(matchingEdges);
        }

        _outgoingEdges = new ReadOnlyDictionary<
            string,
            IReadOnlyList<IAgentGraphEdge>>(outgoing);
    }

    /// <summary>
    /// Gets a registered graph node.
    /// </summary>
    public IAgentGraphNode GetNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        if (_nodes.TryGetValue(nodeId, out var node))
        {
            return node;
        }

        throw new KeyNotFoundException(
            $"Graph '{GraphId}' does not contain " +
            $"node '{nodeId}'.");
    }

    /// <summary>
    /// Attempts to retrieve a graph node.
    /// </summary>
    public bool TryGetNode(
        string nodeId,
        out IAgentGraphNode? node)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        return _nodes.TryGetValue(
            nodeId,
            out node);
    }

    /// <summary>
    /// Gets the outgoing edges for a node.
    /// </summary>
    public IReadOnlyList<IAgentGraphEdge>
        GetOutgoingEdges(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        if (_outgoingEdges.TryGetValue(
            nodeId,
            out var edges))
        {
            return edges;
        }

        throw new KeyNotFoundException(
            $"Graph '{GraphId}' does not contain " +
            $"node '{nodeId}'.");
    }

    /// <summary>
    /// Determines whether a node exists.
    /// </summary>
    public bool ContainsNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        return _nodes.ContainsKey(nodeId);
    }

    /// <summary>
    /// Gets whether a node has no outgoing edges.
    /// </summary>
    public bool IsTerminalNode(string nodeId)
    {
        return GetOutgoingEdges(nodeId).Count == 0;
    }
}

/// <summary>
/// Builds and validates directed agent graphs.
/// </summary>
/// <remarks>
/// The builder validates:
///
/// - Unique node identifiers.
/// - Existence of the entry node.
/// - Existence of edge endpoints.
/// - Reachability of every node from the entry node.
///
/// Cycles are permitted.
///
/// Multiple outgoing edges are permitted.
///
/// Edge evaluation order follows registration order.
///
/// The graph executor defines how eligible
/// outgoing edges are selected.
/// </remarks>
public sealed class AgentGraphBuilder
{
    private readonly Dictionary<
        string,
        IAgentGraphNode> _nodes = new(
            StringComparer.Ordinal);

    private readonly List<
        IAgentGraphEdge> _edges = new();

    private string? _entryNodeId;

    /// <summary>
    /// Gets the graph identifier.
    /// </summary>
    public string GraphId { get; }

    /// <summary>
    /// Gets the number of registered nodes.
    /// </summary>
    public int NodeCount => _nodes.Count;

    /// <summary>
    /// Gets the number of registered edges.
    /// </summary>
    public int EdgeCount => _edges.Count;

    /// <summary>
    /// Initializes a graph builder.
    /// </summary>
    public AgentGraphBuilder(string graphId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            graphId);

        GraphId = graphId.Trim();
    }

    /// <summary>
    /// Registers an executable graph node.
    /// </summary>
    public AgentGraphBuilder AddNode(
        IAgentGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            node.NodeId);

        if (!_nodes.TryAdd(node.NodeId, node))
        {
            throw new InvalidOperationException(
                $"Graph '{GraphId}' already contains " +
                $"node '{node.NodeId}'.");
        }

        return this;
    }

    /// <summary>
    /// Registers multiple graph nodes.
    /// </summary>
    public AgentGraphBuilder AddNodes(
        IEnumerable<IAgentGraphNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (var node in nodes)
        {
            AddNode(node);
        }

        return this;
    }

    /// <summary>
    /// Registers a directed graph edge.
    /// </summary>
    public AgentGraphBuilder AddEdge(
        IAgentGraphEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            edge.SourceNodeId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            edge.TargetNodeId);

        if (string.Equals(
            edge.SourceNodeId,
            edge.TargetNodeId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A graph edge cannot directly connect " +
                "a node to itself.",
                nameof(edge));
        }

        _edges.Add(edge);

        return this;
    }

    /// <summary>
    /// Registers multiple directed edges.
    /// </summary>
    public AgentGraphBuilder AddEdges(
        IEnumerable<IAgentGraphEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);

        foreach (var edge in edges)
        {
            AddEdge(edge);
        }

        return this;
    }

    /// <summary>
    /// Configures the graph entry node.
    /// </summary>
    public AgentGraphBuilder SetEntryNode(
        string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            nodeId);

        _entryNodeId = nodeId.Trim();

        return this;
    }

    /// <summary>
    /// Builds a validated graph definition.
    /// </summary>
    public AgentGraphDefinition Build()
    {
        Validate();

        return new AgentGraphDefinition(
            GraphId,
            _entryNodeId!,
            _nodes,
            _edges);
    }

    /// <summary>
    /// Validates the current graph structure.
    /// </summary>
    public void Validate()
    {
        if (_nodes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Graph '{GraphId}' must contain " +
                "at least one node.");
        }

        if (string.IsNullOrWhiteSpace(_entryNodeId))
        {
            throw new InvalidOperationException(
                $"Graph '{GraphId}' must define " +
                "an entry node.");
        }

        if (!_nodes.ContainsKey(_entryNodeId))
        {
            throw new InvalidOperationException(
                $"Graph '{GraphId}' entry node " +
                $"'{_entryNodeId}' is not registered.");
        }

        ValidateEdges();

        ValidateReachability();
    }

    /// <summary>
    /// Ensures every edge references
    /// registered graph nodes.
    /// </summary>
    private void ValidateEdges()
    {
        foreach (var edge in _edges)
        {
            if (!_nodes.ContainsKey(edge.SourceNodeId))
            {
                throw new InvalidOperationException(
                    $"Graph '{GraphId}' contains an edge " +
                    $"with unknown source node " +
                    $"'{edge.SourceNodeId}'.");
            }

            if (!_nodes.ContainsKey(edge.TargetNodeId))
            {
                throw new InvalidOperationException(
                    $"Graph '{GraphId}' contains an edge " +
                    $"with unknown target node " +
                    $"'{edge.TargetNodeId}'.");
            }
        }
    }

    /// <summary>
    /// Ensures all registered nodes are reachable
    /// from the configured entry node.
    /// </summary>
    private void ValidateReachability()
    {
        var visited = new HashSet<string>(
            StringComparer.Ordinal);

        var pending = new Queue<string>();

        pending.Enqueue(_entryNodeId!);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();

            if (!visited.Add(current))
            {
                continue;
            }

            foreach (var edge in _edges)
            {
                if (!string.Equals(
                    edge.SourceNodeId,
                    current,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                if (!visited.Contains(edge.TargetNodeId))
                {
                    pending.Enqueue(edge.TargetNodeId);
                }
            }
        }

        if (visited.Count == _nodes.Count)
        {
            return;
        }

        var unreachable = _nodes.Keys
            .Where(nodeId => !visited.Contains(nodeId))
            .OrderBy(nodeId => nodeId, StringComparer.Ordinal)
            .ToArray();

        throw new InvalidOperationException(
            $"Graph '{GraphId}' contains unreachable " +
            $"nodes: {string.Join(", ", unreachable)}.");
    }
}