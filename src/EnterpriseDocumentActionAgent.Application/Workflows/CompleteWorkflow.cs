using EnterpriseDocumentActionAgent.Domain.Workflows;

namespace EnterpriseDocumentActionAgent.Application.Workflows;

/// <summary>
/// Represents the result of completing a workflow.
/// </summary>
public sealed record CompleteWorkflowResponse(
    Guid Id,
    string TenantId,
    string Name,
    WorkflowStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Handles the application use case for completing
/// an existing workflow.
/// </summary>
public sealed class CompleteWorkflow
{
    /// <summary>
    /// Completes a workflow that is currently running.
    /// </summary>
    /// <param name="workflow">
    /// The workflow to complete.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// Information about the completed workflow.
    /// </returns>
    public Task<CompleteWorkflowResponse> ExecuteAsync(
        Workflow workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        cancellationToken.ThrowIfCancellationRequested();

        // The domain entity enforces the Running -> Completed
        // transition and records the completion timestamp.
        workflow.Complete();

        if (workflow.StartedAtUtc is not { } startedAtUtc)
        {
            throw new InvalidOperationException(
                "Completed workflow has no start timestamp.");
        }

        if (workflow.FinishedAtUtc is not { } finishedAtUtc)
        {
            throw new InvalidOperationException(
                "Completed workflow has no finish timestamp.");
        }

        return Task.FromResult(
            new CompleteWorkflowResponse(
                Id: workflow.Id,
                TenantId: workflow.TenantId,
                Name: workflow.Name,
                Status: workflow.Status,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: finishedAtUtc,
                UpdatedAtUtc: workflow.UpdatedAtUtc));
    }
}