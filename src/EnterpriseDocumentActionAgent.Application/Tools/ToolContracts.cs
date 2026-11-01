using System.Text.Json;

namespace EnterpriseDocumentActionAgent.Application.Tools;

/// <summary>
/// Defines the outcome of a tool invocation.
/// </summary>
public enum ToolExecutionStatus
{
    Succeeded = 0,
    Failed = 1,
    Denied = 2,
    RequiresApproval = 3
}

/// <summary>
/// Describes a tool available to the agent.
/// </summary>
public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonElement InputSchema,
    bool RequiresApproval = false);

/// <summary>
/// Represents the security context of a tool invocation.
/// </summary>
public sealed record ToolExecutionContext(
    string TenantId,
    string RequestedBy,
    Guid WorkflowId,
    Guid? AgentExecutionId = null);

/// <summary>
/// Represents a request to execute a named tool.
/// </summary>
public sealed record ToolExecutionRequest(
    string ToolName,
    JsonElement Arguments,
    ToolExecutionContext Context);

/// <summary>
/// Represents the result returned by a tool.
/// </summary>
public sealed record ToolExecutionResult(
    string ToolName,
    ToolExecutionStatus Status,
    JsonElement? Output,
    string? Error,
    DateTimeOffset ExecutedAtUtc)
{
    /// <summary>
    /// Creates a successful tool execution result.
    /// </summary>
    public static ToolExecutionResult Success(
        string toolName,
        JsonElement? output = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        return new ToolExecutionResult(
            ToolName: toolName,
            Status: ToolExecutionStatus.Succeeded,
            Output: output,
            Error: null,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a failed tool execution result.
    /// </summary>
    public static ToolExecutionResult Failure(
        string toolName,
        string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        return new ToolExecutionResult(
            ToolName: toolName,
            Status: ToolExecutionStatus.Failed,
            Output: null,
            Error: error,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a result indicating that execution was denied.
    /// </summary>
    public static ToolExecutionResult Denied(
        string toolName,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ToolExecutionResult(
            ToolName: toolName,
            Status: ToolExecutionStatus.Denied,
            Output: null,
            Error: reason,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Creates a result indicating that approval is required.
    /// </summary>
    public static ToolExecutionResult ApprovalRequired(
        string toolName,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ToolExecutionResult(
            ToolName: toolName,
            Status: ToolExecutionStatus.RequiresApproval,
            Output: null,
            Error: reason,
            ExecutedAtUtc: DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Defines a tool that can be invoked by the agent.
/// </summary>
public interface IAgentTool
{
    /// <summary>
    /// Gets the tool's metadata and expected input schema.
    /// </summary>
    ToolDefinition Definition { get; }

    /// <summary>
    /// Executes the tool using the supplied arguments and context.
    /// </summary>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default);
}