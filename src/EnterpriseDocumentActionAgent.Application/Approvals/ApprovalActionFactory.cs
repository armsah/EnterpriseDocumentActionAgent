using System.Text.Json;
using EnterpriseDocumentActionAgent.Application.Tools;

namespace EnterpriseDocumentActionAgent.Application.Approvals;

/// <summary>
/// Defines a factory for creating approval requests
/// from proposed agent tool executions.
/// </summary>
public interface IApprovalActionFactory
{
    /// <summary>
    /// Creates an approval request for a sensitive action.
    /// </summary>
    CreateApprovalRequest Create(
        ToolExecutionRequest toolRequest,
        ApprovalActionType actionType,
        TimeSpan approvalLifetime);
}

/// <summary>
/// Creates validated approval requests for actions
/// that require explicit human authorization.
/// </summary>
/// <remarks>
/// Risk classifications and action types must come from
/// trusted application configuration.
///
/// This factory does not authenticate identities,
/// authorize tenant access, persist approvals,
/// or execute tools.
/// </remarks>
public sealed class ApprovalActionFactory : IApprovalActionFactory
{
    private readonly IActionRiskPolicy _riskPolicy;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a factory using the supplied risk policy.
    /// </summary>
    public ApprovalActionFactory(
        IActionRiskPolicy riskPolicy,
        TimeProvider? timeProvider = null)
    {
        _riskPolicy = riskPolicy
            ?? throw new ArgumentNullException(nameof(riskPolicy));

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public CreateApprovalRequest Create(
        ToolExecutionRequest toolRequest,
        ApprovalActionType actionType,
        TimeSpan approvalLifetime)
    {
        ArgumentNullException.ThrowIfNull(toolRequest);

        if (string.IsNullOrWhiteSpace(toolRequest.ToolName))
        {
            throw new ArgumentException(
                "A valid tool name is required.",
                nameof(toolRequest));
        }

        if (toolRequest.Context is null)
        {
            throw new ArgumentException(
                "A tool execution context is required.",
                nameof(toolRequest));
        }

        if (string.IsNullOrWhiteSpace(
                toolRequest.Context.TenantId))
        {
            throw new ArgumentException(
                "A tenant identifier is required.",
                nameof(toolRequest));
        }

        if (string.IsNullOrWhiteSpace(
                toolRequest.Context.RequestedBy))
        {
            throw new ArgumentException(
                "A requester identifier is required.",
                nameof(toolRequest));
        }

        if (toolRequest.Context.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid workflow identifier is required.",
                nameof(toolRequest));
        }

        if (approvalLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(approvalLifetime),
                "Approval lifetime must be greater than zero.");
        }

        if (!Enum.IsDefined(actionType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(actionType),
                "An unsupported approval action type was supplied.");
        }

        if (toolRequest.Arguments.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Tool arguments must be a JSON object.",
                nameof(toolRequest));
        }

        var toolName = toolRequest.ToolName.Trim();

        var assessment = _riskPolicy.Evaluate(
            new ActionRiskRequest(
                ToolName: toolName,
                ActionType: actionType));

        if (!assessment.RequiresApproval)
        {
            throw new InvalidOperationException(
                "The requested action does not require approval.");
        }

        var now = _timeProvider.GetUtcNow();

        DateTimeOffset expiresAtUtc;

        try
        {
            expiresAtUtc = now.Add(approvalLifetime);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(approvalLifetime),
                approvalLifetime,
                "The approval lifetime exceeds the supported date range.");
        }

        // Clone the JSON so the approval request owns
        // an independent snapshot of the proposed arguments.
        var argumentsSnapshot = toolRequest.Arguments.Clone();

        return new CreateApprovalRequest(
            TenantId: toolRequest.Context.TenantId.Trim(),
            WorkflowId: toolRequest.Context.WorkflowId,
            RequestedBy: toolRequest.Context.RequestedBy.Trim(),
            ToolName: toolName,
            ActionType: actionType,
            Arguments: argumentsSnapshot,
            Reason: assessment.Reason,
            ExpiresAtUtc: expiresAtUtc);
    }
}