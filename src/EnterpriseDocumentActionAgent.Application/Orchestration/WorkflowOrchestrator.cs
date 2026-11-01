using System.Collections.Concurrent;
using EnterpriseDocumentActionAgent.Application.Workflows;
using EnterpriseDocumentActionAgent.Domain.Workflows;

namespace EnterpriseDocumentActionAgent.Application.Orchestration;

/// <summary>
/// Coordinates workflow execution using the existing
/// workflow application handlers and domain lifecycle rules.
/// </summary>
/// <remarks>
/// This is a temporary in-memory orchestration implementation.
///
/// PostgreSQL persistence, secure tool execution, approvals,
/// and Azure OpenAI reasoning will be integrated later.
/// </remarks>
public sealed class WorkflowOrchestrator : IWorkflowOrchestrator
{
    private readonly ConcurrentDictionary<Guid, Workflow> _workflows = new();

    private readonly StartWorkflow _startWorkflow;
    private readonly CompleteWorkflow _completeWorkflow;

    public WorkflowOrchestrator(
        StartWorkflow startWorkflow,
        CompleteWorkflow completeWorkflow)
    {
        _startWorkflow = startWorkflow
            ?? throw new ArgumentNullException(nameof(startWorkflow));

        _completeWorkflow = completeWorkflow
            ?? throw new ArgumentNullException(nameof(completeWorkflow));
    }

    /// <summary>
    /// Registers an existing workflow for temporary in-memory execution.
    /// </summary>
    public void RegisterWorkflow(Workflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);

        if (!_workflows.TryAdd(workflow.Id, workflow))
        {
            throw new InvalidOperationException(
                $"Workflow '{workflow.Id}' is already registered.");
        }
    }

    /// <summary>
    /// Executes a registered workflow.
    /// </summary>
    public async Task<OrchestrationResult> ExecuteAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid workflow ID is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            throw new ArgumentException(
                "Requesting principal is required.",
                nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!_workflows.TryGetValue(request.WorkflowId, out var workflow))
        {
            throw new KeyNotFoundException(
                $"Workflow '{request.WorkflowId}' was not found.");
        }

        // Prevent cross-tenant workflow execution.
        if (!string.Equals(
                workflow.TenantId,
                request.TenantId,
                StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The workflow does not belong to the specified tenant.");
        }

        try
        {
            // Created -> Running
            await _startWorkflow.ExecuteAsync(
                workflow,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Temporary execution behavior.
            //
            // This acknowledges the request but does not perform
            // document retrieval, LLM reasoning, or external actions.
            var output = BuildPlaceholderOutput(
                workflow,
                request.Input);

            cancellationToken.ThrowIfCancellationRequested();

            // Running -> Completed
            await _completeWorkflow.ExecuteAsync(
                workflow,
                cancellationToken);

            return new OrchestrationResult(
                WorkflowId: workflow.Id,
                Status: OrchestrationStatus.Completed,
                Output: output,
                Error: null,
                FinishedAtUtc: workflow.FinishedAtUtc
                    ?? DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // The current domain model does not have a Cancelled
            // state. Mark a running workflow as failed so that
            // it does not remain indefinitely in Running.
            if (workflow.Status == WorkflowStatus.Running)
            {
                workflow.Fail("Workflow execution was cancelled.");
            }

            return new OrchestrationResult(
                WorkflowId: workflow.Id,
                Status: OrchestrationStatus.Cancelled,
                Output: null,
                Error: "Workflow execution was cancelled.",
                FinishedAtUtc: workflow.FinishedAtUtc
                    ?? DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            if (workflow.Status == WorkflowStatus.Running)
            {
                workflow.Fail("Workflow execution failed.");
            }

            return new OrchestrationResult(
                WorkflowId: workflow.Id,
                Status: OrchestrationStatus.Failed,
                Output: null,
                Error: ex.Message,
                FinishedAtUtc: workflow.FinishedAtUtc
                    ?? DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Produces a temporary acknowledgement without claiming
    /// that any enterprise action was performed.
    /// </summary>
    private static string BuildPlaceholderOutput(
        Workflow workflow,
        string? input)
    {
        var taskDescription = string.IsNullOrWhiteSpace(input)
            ? workflow.Description
            : input.Trim();

        return
            $"Workflow '{workflow.Name}' was acknowledged. " +
            $"Requested task: '{taskDescription}'. " +
            "No document search, AI reasoning, or external " +
            "business action was performed.";
    }
}