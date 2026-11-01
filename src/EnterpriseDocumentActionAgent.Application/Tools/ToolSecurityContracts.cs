namespace EnterpriseDocumentActionAgent.Application.Tools;

/// <summary>
/// Describes the outcome of evaluating a tool invocation
/// against an application security policy.
/// </summary>
public enum ToolSecurityDecision
{
    Denied = 0,
    Allowed = 1,
    RequiresApproval = 2
}

/// <summary>
/// Represents the information needed to evaluate whether
/// a requested tool invocation may proceed.
/// </summary>
/// <remarks>
/// The execution context must originate from a trusted,
/// authenticated boundary.
///
/// Caller-supplied tenant and principal identifiers
/// are not sufficient proof of authorization.
/// </remarks>
public sealed record ToolSecurityRequest(
    string ToolName,
    ToolExecutionContext Context,
    bool ToolRequiresApproval);

/// <summary>
/// Represents the result of evaluating a tool security policy.
/// </summary>
public sealed record ToolSecurityResult(
    ToolSecurityDecision Decision,
    string Reason)
{
    /// <summary>
    /// Indicates whether execution may proceed immediately.
    /// </summary>
    public bool IsAllowed =>
        Decision == ToolSecurityDecision.Allowed;

    /// <summary>
    /// Creates a decision permitting execution.
    /// </summary>
    public static ToolSecurityResult Allow(
        string reason = "Tool execution is permitted.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ToolSecurityResult(
            ToolSecurityDecision.Allowed,
            reason);
    }

    /// <summary>
    /// Creates a decision denying execution.
    /// </summary>
    public static ToolSecurityResult Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ToolSecurityResult(
            ToolSecurityDecision.Denied,
            reason);
    }

    /// <summary>
    /// Creates a decision requiring approval
    /// before execution may proceed.
    /// </summary>
    public static ToolSecurityResult RequireApproval(
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ToolSecurityResult(
            ToolSecurityDecision.RequiresApproval,
            reason);
    }
}

/// <summary>
/// Defines the contract for evaluating whether a
/// tool invocation is permitted.
/// </summary>
public interface IToolSecurityPolicy
{
    /// <summary>
    /// Evaluates the security requirements of a tool request.
    /// </summary>
    Task<ToolSecurityResult> EvaluateAsync(
        ToolSecurityRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Evaluates tool access against an explicit allowlist
/// and declared approval requirements.
/// </summary>
/// <remarks>
/// This is a preliminary policy implementation.
///
/// It does not authenticate principals, verify tenant
/// ownership, or validate previously granted approvals.
///
/// A trusted identity boundary and approval service
/// must be integrated before production use.
/// </remarks>
public sealed class DefaultToolSecurityPolicy : IToolSecurityPolicy
{
    private readonly IToolAllowList _allowList;

    public DefaultToolSecurityPolicy(IToolAllowList allowList)
    {
        _allowList = allowList
            ?? throw new ArgumentNullException(nameof(allowList));
    }

    /// <inheritdoc />
    public Task<ToolSecurityResult> EvaluateAsync(
        ToolSecurityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return Task.FromResult(
                ToolSecurityResult.Deny(
                    "A valid tool name is required."));
        }

        if (request.Context is null)
        {
            return Task.FromResult(
                ToolSecurityResult.Deny(
                    "A tool execution context is required."));
        }

        if (request.Context.WorkflowId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Context.TenantId) ||
            string.IsNullOrWhiteSpace(request.Context.RequestedBy))
        {
            return Task.FromResult(
                ToolSecurityResult.Deny(
                    "The tool execution context is incomplete."));
        }

        if (!_allowList.IsAllowed(request.ToolName))
        {
            return Task.FromResult(
                ToolSecurityResult.Deny(
                    "The requested tool is not permitted."));
        }

        if (request.ToolRequiresApproval)
        {
            return Task.FromResult(
                ToolSecurityResult.RequireApproval(
                    "This tool requires explicit approval."));
        }

        return Task.FromResult(
            ToolSecurityResult.Allow());
    }
}