using EnterpriseDocumentActionAgent.Domain.Workflows;

namespace EnterpriseDocumentActionAgent.Application.Workflows;

/// <summary>
/// Represents the information required to create a workflow.
/// </summary>
public sealed record CreateWorkflowRequest(
    string TenantId,
    string Name,
    string Description);

/// <summary>
/// Represents the result of creating a workflow.
/// </summary>
public sealed record CreateWorkflowResponse(
    Guid Id,
    string TenantId,
    string Name,
    string Description,
    WorkflowStatus Status,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Handles the application use case for creating workflows.
/// </summary>
public sealed class CreateWorkflow
{
    /// <summary>
    /// Creates a new workflow using the domain factory.
    /// </summary>
    /// <param name="request">
    /// The workflow creation request.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// A response containing the created workflow's details.
    /// </returns>
    public Task<CreateWorkflowResponse> ExecuteAsync(
        CreateWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        // The domain entity enforces the business rules.
        var workflow = Workflow.Create(
            request.TenantId,
            request.Name,
            request.Description);

        var response = new CreateWorkflowResponse(
            Id: workflow.Id,
            TenantId: workflow.TenantId,
            Name: workflow.Name,
            Description: workflow.Description,
            Status: workflow.Status,
            CreatedAtUtc: workflow.CreatedAtUtc);

        return Task.FromResult(response);
    }
}