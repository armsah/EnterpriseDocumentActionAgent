namespace EnterpriseDocumentActionAgent.Application.Orchestration;

/// <summary>
/// Describes the outcome of a workflow orchestration.
/// </summary>
public enum OrchestrationStatus
{
    Completed = 0,
    Failed = 1,
    Cancelled = 2
}

/// <summary>
/// Represents a request to execute an existing workflow.
/// </summary>
/// <param name="WorkflowId">Identifier of the workflow to execute.</param>
/// <param name="TenantId">Tenant that owns the workflow.</param>
/// <param name="RequestedBy">Identity of the requesting principal.</param>
/// <param name="Input">Optional workflow execution input.</param>
public sealed record OrchestrationRequest(
    Guid WorkflowId,
    string TenantId,
    string RequestedBy,
    string? Input = null);

/// <summary>
/// Represents the result of executing a workflow.
/// </summary>
public sealed record OrchestrationResult(
    Guid WorkflowId,
    OrchestrationStatus Status,
    string? Output,
    string? Error,
    DateTimeOffset FinishedAtUtc);

/// <summary>
/// Defines the contract for executing workflows.
/// </summary>
public interface IWorkflowOrchestrator
{
    /// <summary>
    /// Executes a workflow and returns its outcome.
    /// </summary>
    Task<OrchestrationResult> ExecuteAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken = default);
}