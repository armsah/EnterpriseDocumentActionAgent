using EnterpriseDocumentActionAgent.Domain.Agents;

namespace EnterpriseDocumentActionAgent.Application.Agents;

/// <summary>
/// Represents the outcome of an agent execution.
/// </summary>
public enum AgentExecutionStatus
{
    Completed = 1,
    Rejected = 2,
    StepLimitReached = 3,
    ToolLimitReached = 4,
    Failed = 5
}

/// <summary>
/// Represents a decision returned by an agent reasoning step.
/// </summary>
/// <remarks>
/// This is the execution loop's minimal internal
/// decision contract.
///
/// A richer agent decision model will be introduced
/// separately and can be adapted to this contract.
/// </remarks>
public enum AgentLoopDecisionKind
{
    Complete = 1,
    ExecuteTool = 2,
    Continue = 3,
    Reject = 4
}

/// <summary>
/// Represents one decision from the reasoning component.
/// </summary>
public sealed record AgentLoopDecision(
    AgentLoopDecisionKind Kind,
    string? FinalAnswer = null,
    string? ToolName = null,
    string? ToolInput = null);

/// <summary>
/// Provides the information required for one
/// agent reasoning step.
/// </summary>
public sealed record AgentLoopStepRequest(
    Guid AgentId,
    string TenantId,
    string SubjectId,
    string UserRequest,
    int StepNumber,
    int CompletedToolExecutions,
    IReadOnlyList<AgentLoopToolResult> ToolResults);

/// <summary>
/// Represents a tool execution result supplied
/// to subsequent reasoning steps.
/// </summary>
public sealed record AgentLoopToolResult(
    string ToolName,
    bool Succeeded,
    string? Output);

/// <summary>
/// Defines the reasoning callback used by the
/// agent execution loop.
/// </summary>
public interface IAgentLoopReasoner
{
    Task<AgentLoopDecision> DecideAsync(
        AgentLoopStepRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the tool execution callback used by
/// the agent execution loop.
/// </summary>
/// <remarks>
/// Implementations must enforce authorization,
/// tool allow-lists, approval requirements,
/// and tenant isolation.
///
/// This interface does not itself provide
/// those security guarantees.
/// </remarks>
public interface IAgentLoopToolExecutor
{
    Task<AgentLoopToolResult> ExecuteAsync(
        string tenantId,
        string subjectId,
        string toolName,
        string toolInput,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an agent execution request.
/// </summary>
public sealed record ExecuteAgentRequest(
    Agent Agent,
    string TenantId,
    string SubjectId,
    string UserRequest);

/// <summary>
/// Represents the result of an agent execution.
/// </summary>
public sealed record AgentExecutionResult(
    Guid AgentId,
    AgentExecutionStatus Status,
    string? FinalAnswer,
    int ReasoningSteps,
    int ToolExecutions,
    IReadOnlyList<AgentLoopToolResult> ToolResults);

/// <summary>
/// Coordinates bounded agent reasoning and tool execution.
/// </summary>
/// <remarks>
/// The loop does not authenticate callers or authorize
/// tool execution.
///
/// Those responsibilities belong to the application
/// boundary and the tool executor implementation.
///
/// The loop enforces the agent's configured step
/// and tool execution limits.
///
/// A separate safety policy will be introduced later.
/// </remarks>
public sealed class AgentExecutionLoop
{
    private readonly IAgentLoopReasoner _reasoner;

    private readonly IAgentLoopToolExecutor _toolExecutor;

    public AgentExecutionLoop(
        IAgentLoopReasoner reasoner,
        IAgentLoopToolExecutor toolExecutor)
    {
        _reasoner = reasoner
            ?? throw new ArgumentNullException(
                nameof(reasoner));

        _toolExecutor = toolExecutor
            ?? throw new ArgumentNullException(
                nameof(toolExecutor));
    }

    /// <summary>
    /// Executes an agent request within its configured limits.
    /// </summary>
    public async Task<AgentExecutionResult> ExecuteAsync(
        ExecuteAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArgumentNullException.ThrowIfNull(request.Agent);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequiredText(
            request.TenantId,
            nameof(request.TenantId));

        ValidateRequiredText(
            request.SubjectId,
            nameof(request.SubjectId));

        ValidateRequiredText(
            request.UserRequest,
            nameof(request.UserRequest));

        request.Agent.EnsureTenant(request.TenantId);

        request.Agent.EnsureEnabled();

        var limits = request.Agent.ExecutionLimits;

        limits.Validate();

        var toolResults = new List<AgentLoopToolResult>();

        var reasoningSteps = 0;

        var toolExecutions = 0;

        while (reasoningSteps < limits.MaximumReasoningSteps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            reasoningSteps++;

            var stepRequest = new AgentLoopStepRequest(
                AgentId: request.Agent.Id,
                TenantId: request.TenantId,
                SubjectId: request.SubjectId,
                UserRequest: request.UserRequest,
                StepNumber: reasoningSteps,
                CompletedToolExecutions: toolExecutions,
                ToolResults: toolResults.AsReadOnly());

            AgentLoopDecision decision;

            try
            {
                decision = await _reasoner.DecideAsync(
                    stepRequest,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return CreateResult(
                    request.Agent.Id,
                    AgentExecutionStatus.Failed,
                    finalAnswer: null,
                    reasoningSteps,
                    toolExecutions,
                    toolResults);
            }

            if (decision is null)
            {
                return CreateResult(
                    request.Agent.Id,
                    AgentExecutionStatus.Failed,
                    finalAnswer: null,
                    reasoningSteps,
                    toolExecutions,
                    toolResults);
            }

            switch (decision.Kind)
            {
                case AgentLoopDecisionKind.Complete:
                    if (string.IsNullOrWhiteSpace(
                        decision.FinalAnswer))
                    {
                        return CreateResult(
                            request.Agent.Id,
                            AgentExecutionStatus.Failed,
                            finalAnswer: null,
                            reasoningSteps,
                            toolExecutions,
                            toolResults);
                    }

                    return CreateResult(
                        request.Agent.Id,
                        AgentExecutionStatus.Completed,
                        decision.FinalAnswer,
                        reasoningSteps,
                        toolExecutions,
                        toolResults);

                case AgentLoopDecisionKind.Reject:
                    return CreateResult(
                        request.Agent.Id,
                        AgentExecutionStatus.Rejected,
                        finalAnswer: null,
                        reasoningSteps,
                        toolExecutions,
                        toolResults);

                case AgentLoopDecisionKind.Continue:
                    continue;

                case AgentLoopDecisionKind.ExecuteTool:
                    if (toolExecutions >=
                        limits.MaximumToolExecutions)
                    {
                        return CreateResult(
                            request.Agent.Id,
                            AgentExecutionStatus.ToolLimitReached,
                            finalAnswer: null,
                            reasoningSteps,
                            toolExecutions,
                            toolResults);
                    }

                    if (string.IsNullOrWhiteSpace(
                        decision.ToolName) ||
                        string.IsNullOrWhiteSpace(
                            decision.ToolInput))
                    {
                        return CreateResult(
                            request.Agent.Id,
                            AgentExecutionStatus.Failed,
                            finalAnswer: null,
                            reasoningSteps,
                            toolExecutions,
                            toolResults);
                    }

                    toolExecutions++;

                    AgentLoopToolResult toolResult;

                    try
                    {
                        toolResult = await _toolExecutor.ExecuteAsync(
                            request.TenantId,
                            request.SubjectId,
                            decision.ToolName,
                            decision.ToolInput,
                            cancellationToken);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        return CreateResult(
                            request.Agent.Id,
                            AgentExecutionStatus.Failed,
                            finalAnswer: null,
                            reasoningSteps,
                            toolExecutions,
                            toolResults);
                    }

                    if (toolResult is null)
                    {
                        return CreateResult(
                            request.Agent.Id,
                            AgentExecutionStatus.Failed,
                            finalAnswer: null,
                            reasoningSteps,
                            toolExecutions,
                            toolResults);
                    }

                    toolResults.Add(toolResult);

                    break;

                default:
                    return CreateResult(
                        request.Agent.Id,
                        AgentExecutionStatus.Failed,
                        finalAnswer: null,
                        reasoningSteps,
                        toolExecutions,
                        toolResults);
            }
        }

        return CreateResult(
            request.Agent.Id,
            AgentExecutionStatus.StepLimitReached,
            finalAnswer: null,
            reasoningSteps,
            toolExecutions,
            toolResults);
    }

    private static AgentExecutionResult CreateResult(
        Guid agentId,
        AgentExecutionStatus status,
        string? finalAnswer,
        int reasoningSteps,
        int toolExecutions,
        List<AgentLoopToolResult> toolResults)
    {
        return new AgentExecutionResult(
            AgentId: agentId,
            Status: status,
            FinalAnswer: finalAnswer,
            ReasoningSteps: reasoningSteps,
            ToolExecutions: toolExecutions,
            ToolResults: toolResults.ToArray());
    }

    private static void ValidateRequiredText(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Value cannot be empty.",
                parameterName);
        }
    }
}