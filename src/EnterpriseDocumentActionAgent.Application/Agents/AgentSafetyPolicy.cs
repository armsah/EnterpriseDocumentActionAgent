namespace EnterpriseDocumentActionAgent.Application.Agents;

/// <summary>
/// Represents the outcome of an agent safety evaluation.
/// </summary>
public enum AgentSafetyOutcome
{
    Allowed = 1,
    Rejected = 2,
    ApprovalRequired = 3
}

/// <summary>
/// Defines stable, non-sensitive safety reason codes.
/// </summary>
public enum AgentSafetyReason
{
    None = 0,
    InvalidExecutionContext = 1,
    TenantMismatch = 2,
    SubjectMismatch = 3,
    ExecutionDeadlineReached = 4,
    ReasoningLimitReached = 5,
    ToolLimitReached = 6,
    ToolNotAllowed = 7,
    ToolApprovalRequired = 8,
    InvalidToolProposal = 9,
    UnsupportedDecision = 10
}

/// <summary>
/// Represents the result of evaluating an agent decision.
/// </summary>
public sealed record AgentSafetyEvaluation(
    AgentSafetyOutcome Outcome,
    AgentSafetyReason Reason)
{
    public bool IsAllowed =>
        Outcome == AgentSafetyOutcome.Allowed;

    public bool RequiresApproval =>
        Outcome == AgentSafetyOutcome.ApprovalRequired;

    public static AgentSafetyEvaluation Allow()
    {
        return new AgentSafetyEvaluation(
            AgentSafetyOutcome.Allowed,
            AgentSafetyReason.None);
    }

    public static AgentSafetyEvaluation Reject(
        AgentSafetyReason reason)
    {
        if (reason == AgentSafetyReason.None)
        {
            throw new ArgumentException(
                "A rejection reason is required.",
                nameof(reason));
        }

        return new AgentSafetyEvaluation(
            AgentSafetyOutcome.Rejected,
            reason);
    }

    public static AgentSafetyEvaluation RequireApproval()
    {
        return new AgentSafetyEvaluation(
            AgentSafetyOutcome.ApprovalRequired,
            AgentSafetyReason.ToolApprovalRequired);
    }
}

/// <summary>
/// Defines the policy applied to agent tool proposals.
/// </summary>
/// <remarks>
/// Tool names are matched using ordinal comparison.
///
/// An empty allow-list denies all tool proposals.
///
/// Approval-required tools must also be included
/// in the allow-list.
/// </remarks>
public sealed class AgentSafetyPolicyOptions
{
    public IReadOnlyCollection<string> AllowedToolNames
    {
        get;
        init;
    } = Array.Empty<string>();

    public IReadOnlyCollection<string> ApprovalRequiredToolNames
    {
        get;
        init;
    } = Array.Empty<string>();

    public bool RequireApprovalForAllTools
    {
        get;
        init;
    } = true;
}

/// <summary>
/// Defines the authenticated identity supplied
/// to an agent safety evaluation.
/// </summary>
/// <remarks>
/// These values must come from a trusted authentication
/// and authorization boundary.
///
/// Never construct this identity from unverified
/// client-supplied tenant or subject identifiers.
/// </remarks>
public sealed record AgentSafetyIdentity(
    string TenantId,
    string SubjectId);

/// <summary>
/// Evaluates agent decisions against execution
/// constraints and tool safety configuration.
/// </summary>
/// <remarks>
/// This policy does not authenticate callers,
/// persist approvals, or execute tools.
///
/// ApprovalRequired is not an approval grant.
///
/// The caller must obtain and verify an authorized,
/// action-bound approval before executing the tool.
///
/// Tool gateway authorization remains mandatory,
/// even when this policy returns Allowed.
/// </remarks>
public sealed class AgentSafetyPolicy
{
    private readonly HashSet<string> _allowedToolNames;

    private readonly HashSet<string> _approvalRequiredToolNames;

    private readonly bool _requireApprovalForAllTools;

    public AgentSafetyPolicy(
        AgentSafetyPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ArgumentNullException.ThrowIfNull(
            options.AllowedToolNames);

        ArgumentNullException.ThrowIfNull(
            options.ApprovalRequiredToolNames);

        _allowedToolNames = CreateToolNameSet(
            options.AllowedToolNames,
            nameof(options.AllowedToolNames));

        _approvalRequiredToolNames = CreateToolNameSet(
            options.ApprovalRequiredToolNames,
            nameof(options.ApprovalRequiredToolNames));

        if (!_approvalRequiredToolNames.IsSubsetOf(
            _allowedToolNames))
        {
            throw new ArgumentException(
                "Approval-required tools must also " +
                "be included in the tool allow-list.",
                nameof(options));
        }

        _requireApprovalForAllTools =
            options.RequireApprovalForAllTools;
    }

    /// <summary>
    /// Evaluates a decision for a particular
    /// authenticated execution identity.
    /// </summary>
    public AgentSafetyEvaluation Evaluate(
        AgentDecision decision,
        AgentExecutionSnapshot execution,
        AgentSafetyIdentity identity,
        DateTimeOffset? currentTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(decision);

        ArgumentNullException.ThrowIfNull(execution);

        ArgumentNullException.ThrowIfNull(identity);

        if (!IsValidExecution(execution))
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.InvalidExecutionContext);
        }

        if (string.IsNullOrWhiteSpace(identity.TenantId) ||
            !string.Equals(
                execution.TenantId,
                identity.TenantId,
                StringComparison.Ordinal))
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.TenantMismatch);
        }

        if (string.IsNullOrWhiteSpace(identity.SubjectId) ||
            !string.Equals(
                execution.SubjectId,
                identity.SubjectId,
                StringComparison.Ordinal))
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.SubjectMismatch);
        }

        var now = (
            currentTimeUtc ?? DateTimeOffset.UtcNow
        ).ToUniversalTime();

        if (execution.DeadlineUtc.HasValue &&
            now >= execution.DeadlineUtc.Value)
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.ExecutionDeadlineReached);
        }

        if (execution.ReasoningSteps <= 0 ||
            execution.ReasoningSteps >
                execution.MaximumReasoningSteps)
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.ReasoningLimitReached);
        }

        return decision.Kind switch
        {
            AgentDecisionKind.Complete =>
                EvaluateCompletion(decision),

            AgentDecisionKind.Continue =>
                EvaluateContinuation(execution),

            AgentDecisionKind.Reject =>
                AgentSafetyEvaluation.Reject(
                    AgentSafetyReason.UnsupportedDecision),

            AgentDecisionKind.ProposeTool =>
                EvaluateToolProposal(
                    decision,
                    execution),

            _ => AgentSafetyEvaluation.Reject(
                AgentSafetyReason.UnsupportedDecision)
        };
    }

    private AgentSafetyEvaluation EvaluateCompletion(
        AgentDecision decision)
    {
        if (string.IsNullOrWhiteSpace(
            decision.FinalAnswer))
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.UnsupportedDecision);
        }

        return AgentSafetyEvaluation.Allow();
    }

    private AgentSafetyEvaluation EvaluateContinuation(
        AgentExecutionSnapshot execution)
    {
        if (execution.ReasoningSteps >=
            execution.MaximumReasoningSteps)
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.ReasoningLimitReached);
        }

        return AgentSafetyEvaluation.Allow();
    }

    private AgentSafetyEvaluation EvaluateToolProposal(
        AgentDecision decision,
        AgentExecutionSnapshot execution)
    {
        if (string.IsNullOrWhiteSpace(
            decision.ToolName) ||
            string.IsNullOrWhiteSpace(
                decision.ToolInput))
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.InvalidToolProposal);
        }

        if (decision.ToolName.Length >
                AgentReasoningValidation
                    .MaximumToolNameCharacters ||
            decision.ToolInput.Length >
                AgentReasoningValidation
                    .MaximumAllowedResponseCharacters)
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.InvalidToolProposal);
        }

        if (execution.ToolExecutions >=
            execution.MaximumToolExecutions)
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.ToolLimitReached);
        }

        if (!_allowedToolNames.Contains(
            decision.ToolName))
        {
            return AgentSafetyEvaluation.Reject(
                AgentSafetyReason.ToolNotAllowed);
        }

        if (_requireApprovalForAllTools ||
            _approvalRequiredToolNames.Contains(
                decision.ToolName))
        {
            return AgentSafetyEvaluation.RequireApproval();
        }

        return AgentSafetyEvaluation.Allow();
    }

    private static bool IsValidExecution(
        AgentExecutionSnapshot execution)
    {
        if (execution.ExecutionId == Guid.Empty ||
            execution.AgentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(
                execution.TenantId) ||
            string.IsNullOrWhiteSpace(
                execution.SubjectId) ||
            string.IsNullOrWhiteSpace(
                execution.UserRequest))
        {
            return false;
        }

        if (execution.MaximumReasoningSteps <= 0 ||
            execution.MaximumReasoningSteps > 100 ||
            execution.MaximumToolExecutions < 0 ||
            execution.MaximumToolExecutions > 100)
        {
            return false;
        }

        if (execution.ReasoningSteps < 0 ||
            execution.ToolExecutions < 0 ||
            execution.ReasoningSteps >
                execution.MaximumReasoningSteps ||
            execution.ToolExecutions >
                execution.MaximumToolExecutions)
        {
            return false;
        }

        if (execution.DeadlineUtc.HasValue &&
            execution.DeadlineUtc.Value <
                execution.StartedAtUtc)
        {
            return false;
        }

        if (execution.ToolObservations is null ||
            execution.ToolObservations.Count >
                AgentReasoningValidation
                    .MaximumToolObservations)
        {
            return false;
        }

        return true;
    }

    private static HashSet<string> CreateToolNameSet(
        IReadOnlyCollection<string> names,
        string parameterName)
    {
        var result = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                name.Length >
                    AgentReasoningValidation
                        .MaximumToolNameCharacters ||
                !string.Equals(
                    name,
                    name.Trim(),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Tool names must be nonempty, " +
                    "within the supported length, " +
                    "and must not contain surrounding whitespace.",
                    parameterName);
            }

            if (!result.Add(name))
            {
                throw new ArgumentException(
                    "Duplicate tool names are not allowed.",
                    parameterName);
            }
        }

        return result;
    }
}