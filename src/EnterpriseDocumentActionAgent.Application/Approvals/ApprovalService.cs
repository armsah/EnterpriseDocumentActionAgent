using System.Text.Json;

namespace EnterpriseDocumentActionAgent.Application.Approvals;

/// <summary>
/// Coordinates the creation, retrieval, and review
/// of human approval requests.
/// </summary>
/// <remarks>
/// This service implements application-level approval
/// lifecycle rules.
///
/// Authentication, reviewer authorization, tenant
/// membership verification, and durable persistence
/// must be provided by the hosting application.
///
/// Approval decisions do not automatically authorize
/// tool execution.
/// </remarks>
public sealed class ApprovalService : IApprovalService
{
    private readonly IApprovalStore _approvalStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates an approval service.
    /// </summary>
    public ApprovalService(
        IApprovalStore approvalStore,
        TimeProvider? timeProvider = null)
    {
        _approvalStore = approvalStore
            ?? throw new ArgumentNullException(nameof(approvalStore));

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<ApprovalRequest> RequestApprovalAsync(
        CreateApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateCreateRequest(request);

        var now = _timeProvider.GetUtcNow();

        if (request.ExpiresAtUtc <= now)
        {
            throw new ArgumentException(
                "Approval expiration must be in the future.",
                nameof(request));
        }

        var approval = new ApprovalRequest(
            Id: Guid.NewGuid(),
            TenantId: request.TenantId.Trim(),
            WorkflowId: request.WorkflowId,
            RequestedBy: request.RequestedBy.Trim(),
            ToolName: request.ToolName.Trim(),
            ActionType: request.ActionType,
            Arguments: request.Arguments.Clone(),
            Reason: request.Reason.Trim(),
            Status: ApprovalStatus.Pending,
            CreatedAtUtc: now,
            ExpiresAtUtc: request.ExpiresAtUtc);

        return await _approvalStore.CreateAsync(
            approval,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ApprovalRequest?> GetApprovalAsync(
        Guid approvalId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (approvalId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid approval identifier is required.",
                nameof(approvalId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var approval = await _approvalStore.GetByIdAsync(
            approvalId,
            tenantId.Trim(),
            cancellationToken);

        if (approval is null)
        {
            return null;
        }

        // Expiration is checked at read time.
        // This method does not mutate stored state.
        if (approval.Status == ApprovalStatus.Pending &&
            approval.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return approval with
            {
                Status = ApprovalStatus.Expired
            };
        }

        return approval;
    }

    /// <inheritdoc />
    public async Task<ApprovalReviewResult> ReviewAsync(
        ReviewApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateReviewRequest(request);

        var tenantId = request.TenantId.Trim();
        var reviewerId = request.ReviewedBy.Trim();

        var approval = await _approvalStore.GetByIdAsync(
            request.ApprovalId,
            tenantId,
            cancellationToken);

        if (approval is null)
        {
            throw new KeyNotFoundException(
                "The approval request was not found.");
        }

        if (approval.Status != ApprovalStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only pending approval requests can be reviewed.");
        }

        // A requester cannot approve or reject
        // their own sensitive action.
        if (string.Equals(
                approval.RequestedBy,
                reviewerId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                "The requester cannot review their own approval.");
        }

        var now = _timeProvider.GetUtcNow();

        if (approval.ExpiresAtUtc <= now)
        {
            var expiredApproval = approval with
            {
                Status = ApprovalStatus.Expired
            };

            await _approvalStore.UpdateAsync(
                expiredApproval,
                cancellationToken);

            throw new InvalidOperationException(
                "The approval request has expired.");
        }

        var newStatus = request.Approved
            ? ApprovalStatus.Approved
            : ApprovalStatus.Rejected;

        var updatedApproval = approval with
        {
            Status = newStatus,
            ReviewedBy = reviewerId,
            ReviewedAtUtc = now,
            ReviewComment = request.Comment?.Trim()
        };

        var savedApproval = await _approvalStore.UpdateAsync(
            updatedApproval,
            cancellationToken);

        return new ApprovalReviewResult(
            ApprovalId: savedApproval.Id,
            Status: savedApproval.Status,
            ReviewedBy: savedApproval.ReviewedBy!,
            ReviewedAtUtc: savedApproval.ReviewedAtUtc!.Value,
            Comment: savedApproval.ReviewComment);
    }

    private static void ValidateCreateRequest(
        CreateApprovalRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.TenantId);

        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid workflow identifier is required.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.RequestedBy);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.ToolName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Reason);

        if (!Enum.IsDefined(request.ActionType))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The approval action type is unsupported.");
        }

        if (request.Arguments.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Approval arguments must be a JSON object.",
                nameof(request));
        }
    }

    private static void ValidateReviewRequest(
        ReviewApprovalRequest request)
    {
        if (request.ApprovalId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid approval identifier is required.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.TenantId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.ReviewedBy);
    }
}