namespace EnterpriseDocumentActionAgent.Infrastructure.Persistence;

/// <summary>
/// Represents a serialized snapshot of a workflow aggregate.
/// </summary>
/// <remarks>
/// WorkflowState is an opaque serialized representation
/// of the domain workflow.
///
/// Serialization and deserialization must be implemented
/// by a trusted application or persistence mapper.
///
/// Do not deserialize untrusted workflow state into
/// arbitrary runtime types.
/// </remarks>
public sealed record StoredWorkflowSnapshot(
    Guid WorkflowId,
    string TenantId,
    string WorkflowState,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Represents a workflow persistence failure.
/// </summary>
public sealed class WorkflowRepositoryException : Exception
{
    public WorkflowRepositoryException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Represents an optimistic concurrency conflict
/// while updating a workflow.
/// </summary>
public sealed class WorkflowConcurrencyException : Exception
{
    public WorkflowConcurrencyException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Defines persistence operations for workflow snapshots.
/// </summary>
public interface IWorkflowSnapshotStore
{
    Task AddAsync(
        StoredWorkflowSnapshot workflow,
        CancellationToken cancellationToken = default);

    Task<StoredWorkflowSnapshot?> GetAsync(
        string tenantId,
        Guid workflowId,
        CancellationToken cancellationToken = default);

    Task<StoredWorkflowSnapshot> UpdateAsync(
        StoredWorkflowSnapshot workflow,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredWorkflowSnapshot>> ListAsync(
        string tenantId,
        int maximumResults = 100,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides thread-safe, in-memory persistence
/// for serialized workflow snapshots.
/// </summary>
/// <remarks>
/// This repository is intended for development
/// and local testing.
///
/// Workflow state is stored as a string snapshot,
/// not as a live domain object.
///
/// This prevents callers from mutating stored state
/// through shared object references.
///
/// Data is not durable and is lost when the process exits.
/// </remarks>
public sealed class WorkflowRepository : IWorkflowSnapshotStore
{
    private const int MaximumTenantIdLength = 200;
    private const int MaximumWorkflowStateCharacters = 1000000;
    private const int MaximumListResults = 1000;

    private readonly object _gate = new();

    private readonly Dictionary<
        (string TenantId, Guid WorkflowId),
        StoredWorkflowSnapshot> _workflows = new();

    /// <summary>
    /// Adds a new workflow snapshot.
    /// </summary>
    public Task AddAsync(
        StoredWorkflowSnapshot workflow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateSnapshot(workflow);

        if (workflow.Revision != 0)
        {
            throw new WorkflowRepositoryException(
                "New workflows must begin at revision zero.");
        }

        if (workflow.CreatedAtUtc != workflow.UpdatedAtUtc)
        {
            throw new WorkflowRepositoryException(
                "New workflows must have matching creation and update timestamps.");
        }

        var key = (workflow.TenantId, workflow.WorkflowId);

        lock (_gate)
        {
            if (_workflows.ContainsKey(key))
            {
                throw new WorkflowRepositoryException(
                    "A workflow with this tenant and ID already exists.");
            }

            _workflows.Add(key, workflow);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves a workflow by tenant and ID.
    /// </summary>
    public Task<StoredWorkflowSnapshot?> GetAsync(
        string tenantId,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(tenantId, workflowId);

        lock (_gate)
        {
            _workflows.TryGetValue(
                (tenantId, workflowId),
                out var workflow);

            return Task.FromResult(workflow);
        }
    }

    /// <summary>
    /// Updates a workflow using an expected revision.
    /// </summary>
    /// <remarks>
    /// A successful update increments the revision
    /// exactly once.
    ///
    /// The caller must use the returned snapshot
    /// for subsequent updates.
    /// </remarks>
    public Task<StoredWorkflowSnapshot> UpdateAsync(
        StoredWorkflowSnapshot workflow,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateSnapshot(workflow);

        if (expectedRevision < 0)
        {
            throw new WorkflowRepositoryException(
                "Expected workflow revision cannot be negative.");
        }

        var key = (workflow.TenantId, workflow.WorkflowId);

        lock (_gate)
        {
            if (!_workflows.TryGetValue(key, out var stored))
            {
                throw new WorkflowRepositoryException(
                    "Workflow was not found.");
            }

            if (stored.Revision != expectedRevision)
            {
                throw new WorkflowConcurrencyException(
                    "Workflow was modified by another operation.");
            }

            if (workflow.Revision != expectedRevision)
            {
                throw new WorkflowRepositoryException(
                    "Submitted workflow revision must match the expected revision.");
            }

            if (workflow.CreatedAtUtc != stored.CreatedAtUtc)
            {
                throw new WorkflowRepositoryException(
                    "Workflow creation timestamp cannot change.");
            }

            if (workflow.UpdatedAtUtc < stored.UpdatedAtUtc)
            {
                throw new WorkflowRepositoryException(
                    "Workflow update timestamp cannot move backward.");
            }

            if (expectedRevision == long.MaxValue)
            {
                throw new WorkflowRepositoryException(
                    "Workflow revision cannot be incremented further.");
            }

            var updated = workflow with
            {
                Revision = expectedRevision + 1
            };

            _workflows[key] = updated;

            return Task.FromResult(updated);
        }
    }

    /// <summary>
    /// Lists workflow snapshots belonging to a tenant.
    /// </summary>
    public Task<IReadOnlyList<StoredWorkflowSnapshot>> ListAsync(
        string tenantId,
        int maximumResults = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateTenantId(tenantId);

        if (maximumResults <= 0 ||
            maximumResults > MaximumListResults)
        {
            throw new WorkflowRepositoryException(
                "Maximum workflow results must be between 1 and 1000.");
        }

        lock (_gate)
        {
            IReadOnlyList<StoredWorkflowSnapshot> workflows =
                _workflows
                    .Where(
                        pair =>
                            string.Equals(
                                pair.Key.TenantId,
                                tenantId,
                                StringComparison.Ordinal))
                    .Select(pair => pair.Value)
                    .OrderByDescending(
                        workflow => workflow.UpdatedAtUtc)
                    .ThenBy(
                        workflow => workflow.WorkflowId)
                    .Take(maximumResults)
                    .ToArray();

            return Task.FromResult(workflows);
        }
    }

    private static void ValidateSnapshot(
        StoredWorkflowSnapshot? workflow)
    {
        if (workflow is null)
        {
            throw new WorkflowRepositoryException(
                "Workflow snapshot is required.");
        }

        if (workflow.WorkflowId == Guid.Empty)
        {
            throw new WorkflowRepositoryException(
                "Workflow ID must not be empty.");
        }

        ValidateTenantId(workflow.TenantId);

        if (string.IsNullOrWhiteSpace(workflow.WorkflowState) ||
            workflow.WorkflowState.Length >
                MaximumWorkflowStateCharacters)
        {
            throw new WorkflowRepositoryException(
                "Workflow state is missing or exceeds the allowed size.");
        }

        if (workflow.Revision < 0)
        {
            throw new WorkflowRepositoryException(
                "Workflow revision cannot be negative.");
        }

        ValidateTimestamp(workflow.CreatedAtUtc);
        ValidateTimestamp(workflow.UpdatedAtUtc);

        if (workflow.UpdatedAtUtc < workflow.CreatedAtUtc)
        {
            throw new WorkflowRepositoryException(
                "Workflow update timestamp cannot precede creation.");
        }
    }

    private static void ValidateIdentity(
        string tenantId,
        Guid workflowId)
    {
        ValidateTenantId(tenantId);

        if (workflowId == Guid.Empty)
        {
            throw new WorkflowRepositoryException(
                "Workflow ID must not be empty.");
        }
    }

    private static void ValidateTenantId(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            tenantId.Length > MaximumTenantIdLength)
        {
            throw new WorkflowRepositoryException(
                "Tenant ID is missing or exceeds the allowed length.");
        }
    }

    private static void ValidateTimestamp(
        DateTimeOffset timestamp)
    {
        if (timestamp == default ||
            timestamp.Offset != TimeSpan.Zero)
        {
            throw new WorkflowRepositoryException(
                "Workflow timestamps must be valid UTC values.");
        }
    }
}