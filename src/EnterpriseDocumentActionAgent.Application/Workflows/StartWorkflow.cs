using EnterpriseDocumentActionAgent.Domain.Workflows;

namespace EnterpriseDocumentActionAgent.Application.Workflows;

/// <summary>
/// Represents the result of starting a workflow.
/// </summary>
public sealed record StartWorkflowResponse(
    Guid Id,
    string TenantId,
    string Name,
    WorkflowStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Handles the application use case for starting an existing workflow.
/// </summary>
public sealed class StartWorkflow
{
    /// <summary>
    /// Starts a workflow that is currently in the Created state.
    /// </summary>
    /// <param name="workflow">The workflow to start.</param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// Information about the workflow after it has started.
    /// </returns>
    public Task<StartWorkflowResponse> ExecuteAsync(
        Workflow workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        cancellationToken.ThrowIfCancellationRequested();

        // Enforce the Created -> Running transition
        // through the domain entity.
        workflow.Start();

        if (workflow.StartedAtUtc is not { } startedAtUtc)
        {
            throw new InvalidOperationException(
                "Workflow started without a start timestamp.");
        }

        return Task.FromResult(
            new StartWorkflowResponse(
                Id: workflow.Id,
                TenantId: workflow.TenantId,
                Name: workflow.Name,
                Status: workflow.Status,
                StartedAtUtc: startedAtUtc,
                UpdatedAtUtc: workflow.UpdatedAtUtc));
    }
}