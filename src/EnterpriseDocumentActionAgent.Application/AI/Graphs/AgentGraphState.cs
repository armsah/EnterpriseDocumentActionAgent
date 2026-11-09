using System.Collections.ObjectModel;

namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Represents the execution status of an agent graph.
/// </summary>
public enum AgentGraphExecutionStatus
{
    Created = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4
}

/// <summary>
/// Represents a point-in-time snapshot of
/// an agent graph execution state.
/// </summary>
/// <remarks>
/// Snapshot values are copied into a new dictionary,
/// but referenced objects are not deep-cloned.
///
/// Consumers must use checkpoint-safe value types
/// when snapshots are intended for persistence.
/// </remarks>
public sealed record AgentGraphStateSnapshot
{
    public Guid ExecutionId { get; }

    public string AgentId { get; }

    public Guid? ConversationId { get; }

    public string? CurrentNodeId { get; }

    public AgentGraphExecutionStatus Status { get; }

    public int CompletedSteps { get; }

    public int MaxExecutionSteps { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; }

    public IReadOnlyDictionary<string, object?> Values { get; }

    public AgentGraphStateSnapshot(
        Guid executionId,
        string agentId,
        Guid? conversationId,
        string? currentNodeId,
        AgentGraphExecutionStatus status,
        int completedSteps,
        int maxExecutionSteps,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        IReadOnlyDictionary<string, object?> values)
    {
        if (executionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution ID cannot be empty.",
                nameof(executionId));
        }

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

        if (maxExecutionSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExecutionSteps));
        }

        if (completedSteps < 0 ||
            completedSteps > maxExecutionSteps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedSteps));
        }

        if (updatedAtUtc < createdAtUtc)
        {
            throw new ArgumentException(
                "Update time cannot precede creation time.",
                nameof(updatedAtUtc));
        }

        ExecutionId = executionId;
        AgentId = agentId.Trim();
        ConversationId = conversationId;
        CurrentNodeId = currentNodeId;
        Status = status;
        CompletedSteps = completedSteps;
        MaxExecutionSteps = maxExecutionSteps;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;

        Values = new ReadOnlyDictionary<string, object?>(
            new Dictionary<string, object?>(
                values,
                StringComparer.Ordinal));
    }
}

/// <summary>
/// Stores shared state for a single agent graph execution.
/// </summary>
/// <remarks>
/// Dictionary operations and execution metadata
/// updates are synchronized using a private lock.
///
/// The state container does not guarantee that
/// referenced values are themselves thread-safe.
///
/// Graph execution and checkpoint persistence
/// are handled by separate components.
/// </remarks>
public sealed class AgentGraphState
{
    private readonly object _sync = new();

    private readonly Dictionary<string, object?> _values;

    private string? _currentNodeId;

    private AgentGraphExecutionStatus _status;

    private int _completedSteps;

    private DateTimeOffset _updatedAtUtc;

    public Guid ExecutionId { get; }

    public string AgentId { get; }

    public Guid? ConversationId { get; }

    public int MaxExecutionSteps { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets the current graph node identifier.
    /// </summary>
    public string? CurrentNodeId
    {
        get
        {
            lock (_sync)
            {
                return _currentNodeId;
            }
        }
    }

    /// <summary>
    /// Gets the current execution status.
    /// </summary>
    public AgentGraphExecutionStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    /// <summary>
    /// Gets the number of completed graph steps.
    /// </summary>
    public int CompletedSteps
    {
        get
        {
            lock (_sync)
            {
                return _completedSteps;
            }
        }
    }

    /// <summary>
    /// Gets the last state update time.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _updatedAtUtc;
            }
        }
    }

    /// <summary>
    /// Initializes a new graph execution state.
    /// </summary>
    public AgentGraphState(
        string agentId,
        Guid? conversationId = null,
        Guid? executionId = null,
        int maxExecutionSteps = 100,
        IReadOnlyDictionary<string, object?>? initialValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Conversation ID cannot be empty.",
                nameof(conversationId));
        }

        if (executionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution ID cannot be empty.",
                nameof(executionId));
        }

        if (maxExecutionSteps <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExecutionSteps));
        }

        AgentId = agentId.Trim();

        ConversationId = conversationId;

        ExecutionId = executionId ?? Guid.NewGuid();

        MaxExecutionSteps = maxExecutionSteps;

        CreatedAtUtc = DateTimeOffset.UtcNow;

        _updatedAtUtc = CreatedAtUtc;

        _status = AgentGraphExecutionStatus.Created;

        _values = initialValues is null
            ? new Dictionary<string, object?>(
                StringComparer.Ordinal)
            : new Dictionary<string, object?>(
                initialValues,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Sets or replaces a named state value.
    /// </summary>
    public void SetValue(
        string key,
        object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            _values[key] = value;

            Touch();
        }
    }

    /// <summary>
    /// Attempts to retrieve a typed state value.
    /// </summary>
    public bool TryGetValue<T>(
        string key,
        out T? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            if (_values.TryGetValue(key, out var stored) &&
                stored is T typed)
            {
                value = typed;

                return true;
            }
        }

        value = default;

        return false;
    }

    /// <summary>
    /// Gets a typed value or throws when
    /// the key is absent or its type differs.
    /// </summary>
    public T GetRequiredValue<T>(string key)
    {
        if (TryGetValue<T>(key, out var value))
        {
            return value!;
        }

        throw new KeyNotFoundException(
            $"Graph state key '{key}' is missing " +
            $"or does not contain a value of type " +
            $"'{typeof(T).Name}'.");
    }

    /// <summary>
    /// Determines whether a state key exists.
    /// </summary>
    public bool ContainsKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            return _values.ContainsKey(key);
        }
    }

    /// <summary>
    /// Removes a named state value.
    /// </summary>
    public bool RemoveValue(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            var removed = _values.Remove(key);

            if (removed)
            {
                Touch();
            }

            return removed;
        }
    }

    /// <summary>
    /// Sets the currently active graph node.
    /// </summary>
    public void SetCurrentNode(string nodeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        lock (_sync)
        {
            _currentNodeId = nodeId.Trim();

            Touch();
        }
    }

    /// <summary>
    /// Marks graph execution as running.
    /// </summary>
    public void MarkRunning()
    {
        SetStatus(AgentGraphExecutionStatus.Running);
    }

    /// <summary>
    /// Marks graph execution as paused.
    /// </summary>
    public void MarkPaused()
    {
        SetStatus(AgentGraphExecutionStatus.Paused);
    }

    /// <summary>
    /// Marks graph execution as completed.
    /// </summary>
    public void MarkCompleted()
    {
        SetStatus(AgentGraphExecutionStatus.Completed);
    }

    /// <summary>
    /// Marks graph execution as failed.
    /// </summary>
    public void MarkFailed()
    {
        SetStatus(AgentGraphExecutionStatus.Failed);
    }

    /// <summary>
    /// Records a completed execution step.
    /// </summary>
    public void CompleteStep()
    {
        lock (_sync)
        {
            if (_status != AgentGraphExecutionStatus.Running)
            {
                throw new InvalidOperationException(
                    "Graph must be running to complete a step.");
            }

            if (_completedSteps >= MaxExecutionSteps)
            {
                throw new InvalidOperationException(
                    "Graph execution step limit exceeded.");
            }

            _completedSteps++;

            Touch();
        }
    }

    /// <summary>
    /// Creates a point-in-time state snapshot.
    /// </summary>
    public AgentGraphStateSnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            return new AgentGraphStateSnapshot(
                ExecutionId,
                AgentId,
                ConversationId,
                _currentNodeId,
                _status,
                _completedSteps,
                MaxExecutionSteps,
                CreatedAtUtc,
                _updatedAtUtc,
                _values);
        }
    }

    /// <summary>
    /// Updates the execution status.
    /// </summary>
    private void SetStatus(
        AgentGraphExecutionStatus status)
    {
        lock (_sync)
        {
            if (_status is AgentGraphExecutionStatus.Completed
                or AgentGraphExecutionStatus.Failed)
            {
                throw new InvalidOperationException(
                    "A terminal graph execution cannot " +
                    "change status.");
            }

            if (status == AgentGraphExecutionStatus.Created)
            {
                throw new InvalidOperationException(
                    "Graph execution cannot return " +
                    "to the Created status.");
            }

            _status = status;

            Touch();
        }
    }

    /// <summary>
    /// Updates the last modification timestamp.
    /// Must be called while holding _sync.
    /// </summary>
    private void Touch()
    {
        var now = DateTimeOffset.UtcNow;

        _updatedAtUtc = now > _updatedAtUtc
            ? now
            : _updatedAtUtc;
    }
}