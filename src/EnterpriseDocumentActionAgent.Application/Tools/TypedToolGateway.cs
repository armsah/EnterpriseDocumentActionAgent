namespace EnterpriseDocumentActionAgent.Application.Tools;

/// <summary>
/// Defines the controlled entry point for agent tool execution.
/// </summary>
public interface ITypedToolGateway
{
    /// <summary>
    /// Evaluates and executes a tool request when permitted.
    /// </summary>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates tool resolution, allowlist enforcement,
/// security evaluation, and execution.
/// </summary>
/// <remarks>
/// This gateway enforces preliminary application policies.
///
/// It does not authenticate callers, establish trusted tenant
/// identity, verify resource-level authorization, or validate
/// previously granted human approvals.
///
/// Production callers must integrate those controls before
/// permitting tool execution.
/// </remarks>
public sealed class TypedToolGateway : ITypedToolGateway
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IToolAllowList _allowList;
    private readonly IToolSecurityPolicy _securityPolicy;

    public TypedToolGateway(
        IToolRegistry toolRegistry,
        IToolAllowList allowList,
        IToolSecurityPolicy securityPolicy)
    {
        _toolRegistry = toolRegistry
            ?? throw new ArgumentNullException(nameof(toolRegistry));

        _allowList = allowList
            ?? throw new ArgumentNullException(nameof(allowList));

        _securityPolicy = securityPolicy
            ?? throw new ArgumentNullException(nameof(securityPolicy));
    }

    /// <inheritdoc />
    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return ToolExecutionResult.Denied(
                "Unknown",
                "A valid tool name is required.");
        }

        var toolName = request.ToolName.Trim();

        if (request.Context is null)
        {
            return ToolExecutionResult.Denied(
                toolName,
                "A tool execution context is required.");
        }

        if (request.Context.WorkflowId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Context.TenantId) ||
            string.IsNullOrWhiteSpace(request.Context.RequestedBy))
        {
            return ToolExecutionResult.Denied(
                toolName,
                "The tool execution context is incomplete.");
        }

        // Enforce the explicit allowlist before resolving
        // or executing the requested tool.
        if (!_allowList.IsAllowed(toolName))
        {
            return ToolExecutionResult.Denied(
                toolName,
                "The requested tool is not permitted.");
        }

        if (!_toolRegistry.TryGetTool(toolName, out var tool) ||
            tool is null)
        {
            return ToolExecutionResult.Failure(
                toolName,
                "The requested tool is not registered.");
        }

        // Approval requirements must originate from the
        // registered tool definition, not agent input.
        var securityRequest = new ToolSecurityRequest(
            ToolName: toolName,
            Context: request.Context,
            ToolRequiresApproval: tool.Definition.RequiresApproval);

        ToolSecurityResult securityResult;

        try
        {
            securityResult = await _securityPolicy.EvaluateAsync(
                securityRequest,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Fail closed if security evaluation fails.
            return ToolExecutionResult.Denied(
                toolName,
                "Tool security evaluation failed.");
        }

        if (securityResult is null)
        {
            return ToolExecutionResult.Denied(
                toolName,
                "Tool security evaluation returned no decision.");
        }

        switch (securityResult.Decision)
        {
            case ToolSecurityDecision.Denied:
                return ToolExecutionResult.Denied(
                    toolName,
                    securityResult.Reason);

            case ToolSecurityDecision.RequiresApproval:
                return ToolExecutionResult.ApprovalRequired(
                    toolName,
                    securityResult.Reason);

            case ToolSecurityDecision.Allowed:
                break;

            default:
                // Unknown decisions must never authorize execution.
                return ToolExecutionResult.Denied(
                    toolName,
                    "The security policy returned an unknown decision.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Tool execution occurs only after preliminary
        // policy evaluation has permitted the request.
        try
        {
            var normalizedRequest = request with
            {
                ToolName = toolName
            };

            var result = await _toolRegistry.ExecuteAsync(
                normalizedRequest,
                cancellationToken);

            if (result is null)
            {
                return ToolExecutionResult.Failure(
                    toolName,
                    "The tool returned no execution result.");
            }

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Avoid exposing exception details from tool
            // implementations to untrusted callers.
            return ToolExecutionResult.Failure(
                toolName,
                "Tool execution failed.");
        }
    }
}