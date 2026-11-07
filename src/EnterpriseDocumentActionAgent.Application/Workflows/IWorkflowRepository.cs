using EnterpriseDocumentActionAgent.Domain.Workflows;

namespace EnterpriseDocumentActionAgent.Application.Workflows;

/// <summary>
/// Represents the persisted revision of a workflow.
/// </summary>
/// <remarks>
/// Revisions begin at zero and increase by one
/// after every successful update.
///
/// Callers must obtain the expected revision from
/// a previously retrieved workflow snapshot.
/// </remarks>
public sealed record WorkflowConcurrencyToken(
    long ExpectedRevision);

/// <summary>
/// Represents a workflow aggregate together with
/// its persisted concurrency revision.
/// </summary>
public sealed record PersistedWorkflow(
    Workflow Workflow,
    long Revision);

/// <summary>
/// Represents a workflow persistence failure.
/// </summary>
public class WorkflowPersistenceException : Exception
{
    public WorkflowPersistenceException(string message)
        : base(message)
    {
    }

    public WorkflowPersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Represents an optimistic concurrency conflict
/// while updating a workflow.
/// </summary>
public sealed class WorkflowVersionConflictException
    : WorkflowPersistenceException
{
    public WorkflowVersionConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Defines application-facing persistence operations
/// for workflow aggregates.
/// </summary>
/// <remarks>
/// Implementations must enforce tenant isolation
/// and atomic optimistic concurrency checks.
///
/// The tenant identifier must originate from a
/// trusted authenticated identity.
///
/// Repository-level tenant filtering does not replace
/// authorization checks.
///
/// Implementations must prevent callers from mutating
/// stored workflow state through shared object references.
/// </remarks>
public interface IWorkflowRepository
{
    /// <summary>
    /// Persists a newly created workflow aggregate.
    /// </summary>
    /// <remarks>
    /// A newly created workflow must begin at revision zero.
    ///
    /// The operation must reject duplicate workflow identities
    /// within the same tenant.
    /// </remarks>
    Task AddAsync(
        Workflow workflow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a workflow aggregate within a tenant.
    /// </summary>
    /// <returns>
    /// The workflow and its current revision,
    /// or null when the workflow does not exist
    /// within the requested tenant.
    /// </returns>
    Task<PersistedWorkflow?> GetByIdAsync(
        Guid workflowId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing workflow aggregate using
    /// an expected persistence revision.
    /// </summary>
    /// <remarks>
    /// Implementations must compare the expected
    /// revision with the persisted revision atomically.
    ///
    /// A mismatch must raise
    /// WorkflowVersionConflictException.
    ///
    /// A successful update increments the persisted
    /// revision exactly once.
    ///
    /// The returned value contains the updated aggregate
    /// and its new revision.
    /// </remarks>
    Task<PersistedWorkflow> UpdateAsync(
        Workflow workflow,
        WorkflowConcurrencyToken concurrencyToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists workflows belonging to a tenant.
    /// </summary>
    /// <remarks>
    /// Implementations must enforce the requested
    /// maximum result count.
    ///
    /// Results should be ordered by their latest
    /// modification timestamp, newest first.
    ///
    /// The returned collection must not expose
    /// mutable internal repository state.
    /// </remarks>
    Task<IReadOnlyList<PersistedWorkflow>> ListAsync(
        string tenantId,
        int maximumResults = 100,
        CancellationToken cancellationToken = default);
}