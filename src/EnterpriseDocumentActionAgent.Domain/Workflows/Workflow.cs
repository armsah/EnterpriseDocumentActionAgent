namespace EnterpriseDocumentActionAgent.Domain.Workflows;

/// <summary>
/// Represents the lifecycle states of a workflow.
/// </summary>
public enum WorkflowStatus
{
    Created = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}

/// <summary>
/// Represents a tenant-owned business workflow executed
/// by the Enterprise Document Action Agent.
/// </summary>
public sealed class Workflow
{
    /// <summary>
    /// Unique workflow identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Tenant that owns the workflow.
    /// </summary>
    public string TenantId { get; private set; } = string.Empty;

    /// <summary>
    /// Human-readable workflow name.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Description of the workflow's objective.
    /// </summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Current workflow lifecycle state.
    /// </summary>
    public WorkflowStatus Status { get; private set; }

    /// <summary>
    /// UTC timestamp when the workflow was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// UTC timestamp of the most recent state transition.
    /// </summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// UTC timestamp when execution started.
    /// </summary>
    public DateTimeOffset? StartedAtUtc { get; private set; }

    /// <summary>
    /// UTC timestamp when execution finished.
    /// </summary>
    public DateTimeOffset? FinishedAtUtc { get; private set; }

    /// <summary>
    /// Failure reason, if execution failed.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Reserved for persistence frameworks that require
    /// parameterless construction.
    /// </summary>
    private Workflow()
    {
    }

    private Workflow(
        Guid id,
        string tenantId,
        string name,
        string description)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Description = description;

        Status = WorkflowStatus.Created;

        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    /// <summary>
    /// Creates a new workflow with validated input.
    /// </summary>
    public static Workflow Create(
        string tenantId,
        string name,
        string description)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(tenantId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Workflow name is required.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Workflow description is required.",
                nameof(description));
        }

        return new Workflow(
            Guid.NewGuid(),
            tenantId.Trim(),
            name.Trim(),
            description.Trim());
    }

    /// <summary>
    /// Starts a workflow that is currently in the Created state.
    /// </summary>
    public void Start()
    {
        EnsureStatus(WorkflowStatus.Created, "start");

        Status = WorkflowStatus.Running;
        StartedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = StartedAtUtc.Value;
    }

    /// <summary>
    /// Completes a running workflow.
    /// </summary>
    public void Complete()
    {
        EnsureStatus(WorkflowStatus.Running, "complete");

        Status = WorkflowStatus.Completed;
        FinishedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = FinishedAtUtc.Value;

        FailureReason = null;
    }

    /// <summary>
    /// Marks a running workflow as failed.
    /// </summary>
    public void Fail(string reason)
    {
        EnsureStatus(WorkflowStatus.Running, "fail");

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A failure reason is required.",
                nameof(reason));
        }

        Status = WorkflowStatus.Failed;
        FailureReason = reason.Trim();

        FinishedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = FinishedAtUtc.Value;
    }

    /// <summary>
    /// Ensures that a state transition is permitted.
    /// </summary>
    private void EnsureStatus(
        WorkflowStatus expectedStatus,
        string operation)
    {
        if (Status != expectedStatus)
        {
            throw new InvalidOperationException(
                $"Cannot {operation} workflow '{Id}' " +
                $"while its status is '{Status}'. " +
                $"Expected status: '{expectedStatus}'.");
        }
    }
}