namespace EnterpriseDocumentActionAgent.Application.AI.Graphs;

/// <summary>
/// Represents the possible decisions
/// for a human approval request.
/// </summary>
public enum AgentApprovalDecision
{
    /// <summary>
    /// No final approval decision has been made.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The requested action has been approved.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// The requested action has been rejected.
    /// </summary>
    Rejected = 2
}

/// <summary>
/// Represents a recorded human approval decision.
/// </summary>
/// <remarks>
/// This record stores approval metadata.
///
/// It does not authenticate the approver,
/// verify their permissions, or provide
/// cryptographic integrity protection.
///
/// Approval records must originate from
/// a trusted authorization workflow.
/// </remarks>
public sealed record AgentApprovalRecord
{
    /// <summary>
    /// Gets the approval request identifier.
    /// </summary>
    public string ApprovalRequestId { get; }

    /// <summary>
    /// Gets the associated graph execution identifier.
    /// </summary>
    public Guid ExecutionId { get; }

    /// <summary>
    /// Gets the approval decision.
    /// </summary>
    public AgentApprovalDecision Decision { get; }

    /// <summary>
    /// Gets the approver identifier.
    /// </summary>
    public string? ApproverId { get; }

    /// <summary>
    /// Gets the decision timestamp.
    /// </summary>
    public DateTimeOffset? DecidedAtUtc { get; }

    /// <summary>
    /// Gets an optional decision comment.
    /// </summary>
    public string? Comment { get; }

    /// <summary>
    /// Gets an optional approval policy identifier.
    /// </summary>
    public string? PolicyId { get; }

    /// <summary>
    /// Initializes an approval record.
    /// </summary>
    public AgentApprovalRecord(
        string approvalRequestId,
        Guid executionId,
        AgentApprovalDecision decision,
        string? approverId = null,
        DateTimeOffset? decidedAtUtc = null,
        string? comment = null,
        string? policyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            approvalRequestId);

        if (executionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution ID cannot be empty.",
                nameof(executionId));
        }

        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decision));
        }

        if (decision == AgentApprovalDecision.Pending)
        {
            if (approverId is not null ||
                decidedAtUtc is not null)
            {
                throw new ArgumentException(
                    "A pending approval cannot contain " +
                    "an approver or decision timestamp.");
            }
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                approverId);

            if (decidedAtUtc is null ||
                decidedAtUtc == default(DateTimeOffset))
            {
                throw new ArgumentException(
                    "A final approval decision requires " +
                    "a decision timestamp.",
                    nameof(decidedAtUtc));
            }
        }

        ApprovalRequestId = approvalRequestId.Trim();

        ExecutionId = executionId;

        Decision = decision;

        ApproverId = string.IsNullOrWhiteSpace(approverId)
            ? null
            : approverId.Trim();

        DecidedAtUtc = decidedAtUtc?.ToUniversalTime();

        Comment = string.IsNullOrWhiteSpace(comment)
            ? null
            : comment.Trim();

        PolicyId = string.IsNullOrWhiteSpace(policyId)
            ? null
            : policyId.Trim();
    }

    /// <summary>
    /// Creates a pending approval record.
    /// </summary>
    public static AgentApprovalRecord Pending(
        string approvalRequestId,
        Guid executionId,
        string? policyId = null)
    {
        return new AgentApprovalRecord(
            approvalRequestId,
            executionId,
            AgentApprovalDecision.Pending,
            policyId: policyId);
    }

    /// <summary>
    /// Creates an approved decision record.
    /// </summary>
    public static AgentApprovalRecord Approve(
        string approvalRequestId,
        Guid executionId,
        string approverId,
        string? comment = null,
        string? policyId = null)
    {
        return new AgentApprovalRecord(
            approvalRequestId,
            executionId,
            AgentApprovalDecision.Approved,
            approverId,
            DateTimeOffset.UtcNow,
            comment,
            policyId);
    }

    /// <summary>
    /// Creates a rejected decision record.
    /// </summary>
    public static AgentApprovalRecord Reject(
        string approvalRequestId,
        Guid executionId,
        string approverId,
        string? comment = null,
        string? policyId = null)
    {
        return new AgentApprovalRecord(
            approvalRequestId,
            executionId,
            AgentApprovalDecision.Rejected,
            approverId,
            DateTimeOffset.UtcNow,
            comment,
            policyId);
    }
}

/// <summary>
/// Implements a human approval gate
/// within an agent execution graph.
/// </summary>
/// <remarks>
/// The node reads an approval record from
/// a designated graph-state key.
///
/// If no record exists, or the record
/// is pending, execution is paused.
///
/// If the record is approved, the node
/// completes successfully.
///
/// If the record is rejected, the node
/// reports failure.
///
/// Invalid approval metadata causes
/// the node to report failure.
///
/// The node does not independently verify
/// approver identity, authorization,
/// signatures, or record integrity.
///
/// Approval records must be supplied by
/// a trusted external approval service.
/// </remarks>
public sealed class AgentHumanApprovalNode : IAgentGraphNode
{
    /// <summary>
    /// Gets the default approval-state key prefix.
    /// </summary>
    public const string DefaultStateKeyPrefix =
        "agent.approvals.";

    /// <summary>
    /// Gets the node identifier.
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// Gets the node name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the approval request identifier.
    /// </summary>
    public string ApprovalRequestId { get; }

    /// <summary>
    /// Gets the reason approval is required.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the optional approval policy identifier.
    /// </summary>
    public string? PolicyId { get; }

    /// <summary>
    /// Gets the graph-state key used
    /// to locate the approval record.
    /// </summary>
    public string ApprovalStateKey { get; }

    /// <summary>
    /// Initializes a human approval graph node.
    /// </summary>
    public AgentHumanApprovalNode(
        string nodeId,
        string name,
        string approvalRequestId,
        string reason,
        string? policyId = null,
        string? approvalStateKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            nodeId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            approvalRequestId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            reason);

        NodeId = nodeId.Trim();

        Name = name.Trim();

        ApprovalRequestId = approvalRequestId.Trim();

        Reason = reason.Trim();

        PolicyId = string.IsNullOrWhiteSpace(policyId)
            ? null
            : policyId.Trim();

        ApprovalStateKey =
            string.IsNullOrWhiteSpace(approvalStateKey)
                ? DefaultStateKeyPrefix + ApprovalRequestId
                : approvalStateKey.Trim();
    }

    /// <summary>
    /// Evaluates the recorded approval decision.
    /// </summary>
    public Task<AgentGraphNodeResult> ExecuteAsync(
        AgentGraphState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        cancellationToken.ThrowIfCancellationRequested();

        if (!state.ContainsKey(ApprovalStateKey))
        {
            return Task.FromResult(
                AgentGraphNodeResult.Pause(
                    BuildPendingMessage()));
        }

        if (!state.TryGetValue<AgentApprovalRecord>(
            ApprovalStateKey,
            out var approval) ||
            approval is null)
        {
            return Task.FromResult(
                AgentGraphNodeResult.Failure(
                    $"Approval state '{ApprovalStateKey}' " +
                    "does not contain a valid approval record."));
        }

        if (!string.Equals(
            approval.ApprovalRequestId,
            ApprovalRequestId,
            StringComparison.Ordinal))
        {
            return Task.FromResult(
                AgentGraphNodeResult.Failure(
                    "Approval request identifier mismatch."));
        }

        if (approval.ExecutionId != state.ExecutionId)
        {
            return Task.FromResult(
                AgentGraphNodeResult.Failure(
                    "Approval record belongs to " +
                    "a different graph execution."));
        }

        if (PolicyId is not null &&
            !string.Equals(
                approval.PolicyId,
                PolicyId,
                StringComparison.Ordinal))
        {
            return Task.FromResult(
                AgentGraphNodeResult.Failure(
                    "Approval policy identifier mismatch."));
        }

        var result = approval.Decision switch
        {
            AgentApprovalDecision.Pending =>
                AgentGraphNodeResult.Pause(
                    BuildPendingMessage()),

            AgentApprovalDecision.Approved =>
                AgentGraphNodeResult.Success(
                    $"Approval request '{ApprovalRequestId}' " +
                    $"was approved by '{approval.ApproverId}'."),

            AgentApprovalDecision.Rejected =>
                AgentGraphNodeResult.Failure(
                    $"Approval request '{ApprovalRequestId}' " +
                    $"was rejected by '{approval.ApproverId}'."),

            _ =>
                AgentGraphNodeResult.Failure(
                    "Unsupported approval decision.")
        };

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(result);
    }

    /// <summary>
    /// Builds a diagnostic message for
    /// a pending approval request.
    /// </summary>
    private string BuildPendingMessage()
    {
        return
            $"Human approval required for request " +
            $"'{ApprovalRequestId}'. Reason: {Reason}";
    }
}