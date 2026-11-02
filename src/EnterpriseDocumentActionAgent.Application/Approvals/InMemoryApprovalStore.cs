using System.Collections.Concurrent;
using System.Text.Json;

namespace EnterpriseDocumentActionAgent.Application.Approvals;

/// <summary>
/// Provides thread-safe, in-memory storage for approval requests.
/// </summary>
/// <remarks>
/// This implementation is intended for development and testing.
///
/// Approval requests are not persisted across application restarts.
///
/// Tenant identifiers are used to scope reads and updates,
/// but authentication and authorization must be enforced
/// by the calling application.
///
/// Concurrent updates to the same approval are serialized,
/// but this store does not provide durable transactions.
/// </remarks>
public sealed class InMemoryApprovalStore : IApprovalStore
{
    private readonly ConcurrentDictionary<Guid, ApprovalRequest>
        _approvals = new();

    private readonly object _syncRoot = new();

    /// <inheritdoc />
    public Task<ApprovalRequest> CreateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateApproval(request);

        if (request.Status != ApprovalStatus.Pending)
        {
            throw new ArgumentException(
                "New approval requests must be pending.",
                nameof(request));
        }

        var snapshot = CloneApproval(request);

        if (!_approvals.TryAdd(snapshot.Id, snapshot))
        {
            throw new InvalidOperationException(
                "An approval with the same identifier already exists.");
        }

        return Task.FromResult(CloneApproval(snapshot));
    }

    /// <inheritdoc />
    public Task<ApprovalRequest?> GetByIdAsync(
        Guid approvalId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateTenantId(tenantId);

        if (approvalId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid approval identifier is required.",
                nameof(approvalId));
        }

        if (!_approvals.TryGetValue(
                approvalId,
                out var approval) ||
            !string.Equals(
                approval.TenantId,
                tenantId.Trim(),
                StringComparison.Ordinal))
        {
            return Task.FromResult<ApprovalRequest?>(null);
        }

        return Task.FromResult<ApprovalRequest?>(
            CloneApproval(approval));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApprovalRequest>> GetByWorkflowIdAsync(
        Guid workflowId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateTenantId(tenantId);

        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid workflow identifier is required.",
                nameof(workflowId));
        }

        var normalizedTenantId = tenantId.Trim();

        var approvals = _approvals.Values
            .Where(approval =>
                approval.WorkflowId == workflowId &&
                string.Equals(
                    approval.TenantId,
                    normalizedTenantId,
                    StringComparison.Ordinal))
            .OrderBy(approval => approval.CreatedAtUtc)
            .ThenBy(approval => approval.Id)
            .Select(CloneApproval)
            .ToArray();

        return Task.FromResult<IReadOnlyList<ApprovalRequest>>(
            approvals);
    }

    /// <inheritdoc />
    public Task<ApprovalRequest> UpdateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateApproval(request);

        lock (_syncRoot)
        {
            if (!_approvals.TryGetValue(
                    request.Id,
                    out var existing))
            {
                throw new KeyNotFoundException(
                    "The approval request does not exist.");
            }

            if (!string.Equals(
                    existing.TenantId,
                    request.TenantId,
                    StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException(
                    "An approval cannot be moved to another tenant.");
            }

            if (existing.WorkflowId != request.WorkflowId ||
                !string.Equals(
                    existing.RequestedBy,
                    request.RequestedBy,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.ToolName,
                    request.ToolName,
                    StringComparison.Ordinal) ||
                existing.ActionType != request.ActionType ||
                existing.CreatedAtUtc != request.CreatedAtUtc ||
                existing.ExpiresAtUtc != request.ExpiresAtUtc ||
                !string.Equals(
                    existing.Reason,
                    request.Reason,
                    StringComparison.Ordinal) ||
                !JsonElementsEqual(
                    existing.Arguments,
                    request.Arguments))
            {
                throw new InvalidOperationException(
                    "The original approval action cannot be modified.");
            }

            ValidateStatusTransition(existing, request);

            var snapshot = CloneApproval(request);

            _approvals[request.Id] = snapshot;

            return Task.FromResult(CloneApproval(snapshot));
        }
    }

    private static void ValidateApproval(
        ApprovalRequest request)
    {
        if (request.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid approval identifier is required.",
                nameof(request));
        }

        ValidateTenantId(request.TenantId);

        if (request.WorkflowId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid workflow identifier is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RequestedBy))
        {
            throw new ArgumentException(
                "A requester identifier is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            throw new ArgumentException(
                "A tool name is required.",
                nameof(request));
        }

        if (!Enum.IsDefined(request.ActionType) ||
            !Enum.IsDefined(request.Status))
        {
            throw new ArgumentException(
                "The approval contains an unsupported enum value.",
                nameof(request));
        }

        if (request.Arguments.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "Approval arguments must be a JSON object.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ArgumentException(
                "An approval reason is required.",
                nameof(request));
        }

        if (request.CreatedAtUtc == default ||
            request.ExpiresAtUtc <= request.CreatedAtUtc)
        {
            throw new ArgumentException(
                "The approval timestamps are invalid.",
                nameof(request));
        }
    }

    private static void ValidateTenantId(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
    }

    private static ApprovalRequest CloneApproval(
        ApprovalRequest request)
    {
        return request with
        {
            Arguments = request.Arguments.Clone()
        };
    }

    private static void ValidateStatusTransition(
        ApprovalRequest existing,
        ApprovalRequest updated)
    {
        if (existing.Status != ApprovalStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only pending approvals can be updated.");
        }

        if (updated.Status == ApprovalStatus.Pending)
        {
            throw new InvalidOperationException(
                "An approval update must change its status.");
        }

        if (updated.Status == ApprovalStatus.Approved ||
            updated.Status == ApprovalStatus.Rejected)
        {
            if (string.IsNullOrWhiteSpace(updated.ReviewedBy) ||
                updated.ReviewedAtUtc is null)
            {
                throw new InvalidOperationException(
                    "A review decision requires reviewer details.");
            }

            if (updated.ReviewedAtUtc < updated.CreatedAtUtc)
            {
                throw new InvalidOperationException(
                    "Review time cannot precede creation time.");
            }

            if (updated.ReviewedAtUtc >= updated.ExpiresAtUtc)
            {
                throw new InvalidOperationException(
                    "An expired approval cannot be reviewed.");
            }
        }
        else if (updated.Status != ApprovalStatus.Expired &&
                 updated.Status != ApprovalStatus.Cancelled)
        {
            throw new InvalidOperationException(
                "The requested approval status is unsupported.");
        }
    }

    private static bool JsonElementsEqual(
        JsonElement first,
        JsonElement second)
    {
        return string.Equals(
            first.GetRawText(),
            second.GetRawText(),
            StringComparison.Ordinal);
    }
}