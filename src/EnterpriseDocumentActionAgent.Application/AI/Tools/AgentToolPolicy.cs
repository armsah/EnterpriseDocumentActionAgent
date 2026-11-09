using System.Collections.Frozen;

namespace EnterpriseDocumentActionAgent.Application.AI.Tools;

/// <summary>
/// Represents the outcome of a tool policy evaluation.
/// </summary>
public enum AgentToolPolicyDecision
{
    Denied = 0,
    Allowed = 1,
    RequiresApproval = 2
}

/// <summary>
/// Describes the result of evaluating a tool request.
/// </summary>
public sealed record AgentToolPolicyResult
{
    public string AgentId { get; }

    public string ToolName { get; }

    public AgentToolPolicyDecision Decision { get; }

    public string Reason { get; }

    public bool IsAllowed =>
        Decision == AgentToolPolicyDecision.Allowed;

    public bool RequiresApproval =>
        Decision == AgentToolPolicyDecision.RequiresApproval;

    public AgentToolPolicyResult(
        string agentId,
        string toolName,
        AgentToolPolicyDecision decision,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(
                nameof(decision));
        }

        AgentId = agentId;
        ToolName = toolName;
        Decision = decision;
        Reason = reason;
    }
}

/// <summary>
/// Defines immutable, default-deny tool permissions
/// for an AI agent.
/// </summary>
/// <remarks>
/// Policy evaluation is not equivalent to execution
/// authorization. Callers must enforce the result
/// and independently verify user permissions,
/// tenant boundaries, and approval decisions.
/// </remarks>
public sealed class AgentToolPolicy
{
    public string AgentId { get; }

    public IReadOnlySet<string> AllowedTools { get; }

    public IReadOnlySet<string> DeniedTools { get; }

    public IReadOnlySet<string> ApprovalRequiredTools { get; }

    public AgentToolPolicy(
        string agentId,
        IEnumerable<string> allowedTools,
        IEnumerable<string>? deniedTools = null,
        IEnumerable<string>? approvalRequiredTools = null)
    {
        ValidateIdentifier(agentId, nameof(agentId));

        ArgumentNullException.ThrowIfNull(allowedTools);

        var allowed = CreateToolSet(
            allowedTools,
            nameof(allowedTools));

        var denied = CreateToolSet(
            deniedTools,
            nameof(deniedTools));

        var approvalRequired = CreateToolSet(
            approvalRequiredTools,
            nameof(approvalRequiredTools));

        var conflicts = allowed
            .Intersect(
                denied,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (conflicts.Length > 0)
        {
            throw new ArgumentException(
                "A tool cannot be both allowed and denied: " +
                string.Join(", ", conflicts));
        }

        var invalidApprovalTools = approvalRequired
            .Except(
                allowed,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (invalidApprovalTools.Length > 0)
        {
            throw new ArgumentException(
                "Approval-required tools must appear " +
                "in the allowlist: " +
                string.Join(", ", invalidApprovalTools));
        }

        AgentId = agentId;
        AllowedTools = allowed;
        DeniedTools = denied;
        ApprovalRequiredTools = approvalRequired;
    }

    /// <summary>
    /// Evaluates a requested tool against this policy.
    /// </summary>
    public AgentToolPolicyResult Evaluate(string toolName)
    {
        ValidateIdentifier(toolName, nameof(toolName));

        if (DeniedTools.Contains(toolName))
        {
            return new AgentToolPolicyResult(
                AgentId,
                toolName,
                AgentToolPolicyDecision.Denied,
                "Tool is explicitly denied.");
        }

        if (!AllowedTools.Contains(toolName))
        {
            return new AgentToolPolicyResult(
                AgentId,
                toolName,
                AgentToolPolicyDecision.Denied,
                "Tool is not included in the allowlist.");
        }

        if (ApprovalRequiredTools.Contains(toolName))
        {
            return new AgentToolPolicyResult(
                AgentId,
                toolName,
                AgentToolPolicyDecision.RequiresApproval,
                "Tool requires an explicit approval decision.");
        }

        return new AgentToolPolicyResult(
            AgentId,
            toolName,
            AgentToolPolicyDecision.Allowed,
            "Tool is permitted by the agent policy.");
    }

    /// <summary>
    /// Returns true only when the tool may execute
    /// without an additional approval decision.
    /// </summary>
    public bool CanExecuteWithoutApproval(string toolName)
    {
        return Evaluate(toolName).IsAllowed;
    }

    /// <summary>
    /// Throws when a tool is not immediately permitted.
    /// </summary>
    /// <remarks>
    /// Approval-required tools are rejected here until
    /// a separate approval workflow authorizes execution.
    /// </remarks>
    public void RequireExecutionPermission(string toolName)
    {
        var result = Evaluate(toolName);

        if (result.Decision != AgentToolPolicyDecision.Allowed)
        {
            throw new UnauthorizedAccessException(
                $"Agent '{AgentId}' cannot execute " +
                $"tool '{toolName}': {result.Reason}");
        }
    }

    private static FrozenSet<string> CreateToolSet(
        IEnumerable<string>? toolNames,
        string parameterName)
    {
        var unique = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (toolNames is not null)
        {
            foreach (var toolName in toolNames)
            {
                ValidateIdentifier(toolName, parameterName);

                unique.Add(toolName);
            }
        }

        return unique.ToFrozenSet(
            StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateIdentifier(
        string? identifier,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException(
                "Identifier is required.",
                parameterName);
        }

        if (identifier.Length > 200)
        {
            throw new ArgumentException(
                "Identifier cannot exceed 200 characters.",
                parameterName);
        }
    }
}