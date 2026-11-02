using System.Text.Json;

namespace EnterpriseDocumentActionAgent.Application.Approvals;

/// <summary>
/// Represents the lifecycle state of a human approval request.
/// </summary>
public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Expired = 3,
    Cancelled = 4
}

/// <summary>
/// Describes the type of action requiring approval.
/// </summary>
public enum ApprovalActionType
{
    ToolExecution = 0,
    DocumentModification = 1,
    DocumentDeletion = 2,
    ExternalAction = 3
}

/// <summary>
/// Represents a request to obtain human approval
/// before executing a sensitive action.
/// </summary>
public sealed record CreateApprovalRequest(
    string TenantId,
    Guid WorkflowId,
    string RequestedBy,
    string ToolName,
    ApprovalActionType ActionType,
    JsonElement Arguments,
    string Reason,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Represents a stored human approval request.
/// </summary>
public sealed record ApprovalRequest(
    Guid Id,
    string TenantId,
    Guid WorkflowId,
    string RequestedBy,
    string ToolName,
    ApprovalActionType ActionType,
    JsonElement Arguments,
    string Reason,
    ApprovalStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAtUtc = null,
    string? ReviewComment = null);

/// <summary>
/// Represents a human review decision.
/// </summary>
public sealed record ReviewApprovalRequest(
    Guid ApprovalId,
    string TenantId,
    string ReviewedBy,
    bool Approved,
    string? Comment = null);

/// <summary>
/// Represents the result of reviewing an approval request.
/// </summary>
public sealed record ApprovalReviewResult(
    Guid ApprovalId,
    ApprovalStatus Status,
    string ReviewedBy,
    DateTimeOffset ReviewedAtUtc,
    string? Comment);

/// <summary>
/// Defines operations for storing and retrieving approval requests.
/// </summary>
public interface IApprovalStore
{
    /// <summary>
    /// Persists a new approval request.
    /// </summary>
    Task<ApprovalRequest> CreateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an approval request by ID and tenant.
    /// </summary>
    Task<ApprovalRequest?> GetByIdAsync(
        Guid approvalId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves approval requests belonging to a workflow.
    /// </summary>
    Task<IReadOnlyList<ApprovalRequest>> GetByWorkflowIdAsync(
        Guid workflowId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing approval request.
    /// </summary>
    Task<ApprovalRequest> UpdateAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the application service responsible for
/// creating and reviewing human approval requests.
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Creates a pending approval request.
    /// </summary>
    Task<ApprovalRequest> RequestApprovalAsync(
        CreateApprovalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an approval request.
    /// </summary>
    Task<ApprovalRequest?> GetApprovalAsync(
        Guid approvalId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reviews an existing approval request.
    /// </summary>
    Task<ApprovalReviewResult> ReviewAsync(
        ReviewApprovalRequest request,
        CancellationToken cancellationToken = default);
}