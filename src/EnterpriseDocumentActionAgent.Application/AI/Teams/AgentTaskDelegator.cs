using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace EnterpriseDocumentActionAgent.Application.AI.Teams;

/// <summary>
/// Represents a task submitted for delegation
/// to a member of an agent team.
/// </summary>
/// <remarks>
/// The payload is application-defined.
///
/// This request does not grant permission
/// to access resources or execute tools.
/// </remarks>
public sealed record AgentDelegationRequest
{
    /// <summary>
    /// Gets the unique task identifier.
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// Gets the task description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the task payload.
    /// </summary>
    public object? Payload { get; }

    /// <summary>
    /// Gets an optional required agent role.
    /// </summary>
    public string? RequiredRole { get; }

    /// <summary>
    /// Gets an optional preferred agent ID.
    /// </summary>
    public string? PreferredAgentId { get; }

    /// <summary>
    /// Gets whether the supervisor may
    /// be selected as a worker.
    /// </summary>
    public bool AllowSupervisorSelection { get; }

    /// <summary>
    /// Gets excluded agent identifiers.
    /// </summary>
    public IReadOnlyList<string> ExcludedAgentIds { get; }

    /// <summary>
    /// Initializes a delegation request.
    /// </summary>
    public AgentDelegationRequest(
        string taskId,
        string description,
        object? payload = null,
        string? requiredRole = null,
        string? preferredAgentId = null,
        bool allowSupervisorSelection = false,
        IEnumerable<string>? excludedAgentIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        TaskId = taskId.Trim();

        Description = description.Trim();

        Payload = payload;

        RequiredRole = string.IsNullOrWhiteSpace(requiredRole)
            ? null
            : requiredRole.Trim();

        PreferredAgentId =
            string.IsNullOrWhiteSpace(preferredAgentId)
                ? null
                : preferredAgentId.Trim();

        AllowSupervisorSelection =
            allowSupervisorSelection;

        var excluded = new HashSet<string>(
            StringComparer.Ordinal);

        if (excludedAgentIds is not null)
        {
            foreach (var agentId in excludedAgentIds)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    agentId);

                excluded.Add(agentId.Trim());
            }
        }

        ExcludedAgentIds = Array.AsReadOnly(
            excluded.OrderBy(
                id => id,
                StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>
    /// Converts this request into a
    /// supervisor routing request.
    /// </summary>
    public AgentSupervisorRequest ToSupervisorRequest()
    {
        return new AgentSupervisorRequest(
            taskId: TaskId,
            description: Description,
            requiredRole: RequiredRole,
            preferredAgentId: PreferredAgentId,
            allowSupervisorSelection:
                AllowSupervisorSelection,
            excludedAgentIds: ExcludedAgentIds);
    }
}

/// <summary>
/// Represents the outcome of executing
/// a delegated task.
/// </summary>
public sealed record AgentDelegationResult
{
    /// <summary>
    /// Gets the task identifier.
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// Gets the team identifier.
    /// </summary>
    public string TeamId { get; }

    /// <summary>
    /// Gets the selected agent identifier.
    /// </summary>
    public string AgentId { get; }

    /// <summary>
    /// Gets whether execution succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the task output.
    /// </summary>
    public object? Output { get; }

    /// <summary>
    /// Gets an optional execution message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the delegation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets the completion timestamp.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Gets the routing decision.
    /// </summary>
    public AgentSupervisorDecision RoutingDecision { get; }

    /// <summary>
    /// Initializes a delegation result.
    /// </summary>
    public AgentDelegationResult(
        AgentSupervisorDecision routingDecision,
        bool succeeded,
        object? output,
        string? message,
        DateTimeOffset createdAtUtc,
        DateTimeOffset completedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(
            routingDecision);

        if (completedAtUtc < createdAtUtc)
        {
            throw new ArgumentException(
                "Completion time cannot precede " +
                "delegation creation time.",
                nameof(completedAtUtc));
        }

        RoutingDecision = routingDecision;

        TaskId = routingDecision.TaskId;

        TeamId = routingDecision.TeamId;

        AgentId = routingDecision.SelectedAgentId;

        Succeeded = succeeded;

        Output = output;

        Message = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();

        CreatedAtUtc = createdAtUtc.ToUniversalTime();

        CompletedAtUtc = completedAtUtc.ToUniversalTime();
    }
}

/// <summary>
/// Defines an execution handler for
/// delegated agent tasks.
/// </summary>
/// <remarks>
/// Implementations are responsible for
/// authorization, resource access,
/// and task-specific execution behavior.
/// </remarks>
public interface IAgentTaskHandler
{
    /// <summary>
    /// Gets the agent identifier supported
    /// by this handler.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Executes a delegated task.
    /// </summary>
    Task<AgentTaskHandlerResult> ExecuteAsync(
        AgentDelegationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a result returned by
/// an agent task handler.
/// </summary>
public sealed record AgentTaskHandlerResult
{
    /// <summary>
    /// Gets whether the handler succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the handler output.
    /// </summary>
    public object? Output { get; }

    /// <summary>
    /// Gets an optional diagnostic message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Initializes a handler result.
    /// </summary>
    public AgentTaskHandlerResult(
        bool succeeded,
        object? output = null,
        string? message = null)
    {
        Succeeded = succeeded;

        Output = output;

        Message = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();
    }

    /// <summary>
    /// Creates a successful handler result.
    /// </summary>
    public static AgentTaskHandlerResult Success(
        object? output = null,
        string? message = null)
    {
        return new AgentTaskHandlerResult(
            true,
            output,
            message);
    }

    /// <summary>
    /// Creates a failed handler result.
    /// </summary>
    public static AgentTaskHandlerResult Failure(
        string message,
        object? output = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            message);

        return new AgentTaskHandlerResult(
            false,
            output,
            message);
    }
}

/// <summary>
/// Coordinates task routing and execution
/// through registered agent handlers.
/// </summary>
/// <remarks>
/// The delegator uses AgentSupervisor to
/// select an eligible team member.
///
/// A matching handler must be registered
/// before the task can be executed.
///
/// Handler registrations are thread-safe.
///
/// The delegator does not enforce
/// authorization or tenant isolation.
///
/// Handler exceptions propagate to callers.
/// </remarks>
public sealed class AgentTaskDelegator
{
    private readonly AgentSupervisor _supervisor;

    private readonly ConcurrentDictionary<
        string,
        IAgentTaskHandler> _handlers =
            new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the supervisor used for routing.
    /// </summary>
    public AgentSupervisor Supervisor => _supervisor;

    /// <summary>
    /// Gets the number of registered handlers.
    /// </summary>
    public int HandlerCount => _handlers.Count;

    /// <summary>
    /// Initializes a task delegator.
    /// </summary>
    public AgentTaskDelegator(
        AgentSupervisor supervisor)
    {
        ArgumentNullException.ThrowIfNull(supervisor);

        _supervisor = supervisor;
    }

    /// <summary>
    /// Registers an agent task handler.
    /// </summary>
    public AgentTaskDelegator RegisterHandler(
        IAgentTaskHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            handler.AgentId);

        var agentId = handler.AgentId.Trim();

        if (!_handlers.TryAdd(agentId, handler))
        {
            throw new InvalidOperationException(
                $"A handler for agent '{agentId}' " +
                "is already registered.");
        }

        return this;
    }

    /// <summary>
    /// Gets whether a handler is registered
    /// for the specified agent.
    /// </summary>
    public bool ContainsHandler(
        string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        return _handlers.ContainsKey(agentId.Trim());
    }

    /// <summary>
    /// Removes a registered handler.
    /// </summary>
    public bool UnregisterHandler(
        string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        return _handlers.TryRemove(
            agentId.Trim(),
            out _);
    }

    /// <summary>
    /// Gets registered handler identifiers.
    /// </summary>
    public IReadOnlyList<string> GetHandlerIds()
    {
        return Array.AsReadOnly(
            _handlers.Keys
                .OrderBy(
                    id => id,
                    StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Routes and executes a delegated task.
    /// </summary>
    /// <remarks>
    /// Selection and handler lookup are
    /// separate operations.
    ///
    /// Concurrent membership changes may
    /// invalidate a routing decision.
    ///
    /// Callers must ensure appropriate
    /// authorization before delegation.
    /// </remarks>
    public async Task<AgentDelegationResult> DelegateAsync(
        AgentDelegationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        var startedAtUtc = DateTimeOffset.UtcNow;

        var routingRequest =
            request.ToSupervisorRequest();

        var decision = await _supervisor
            .SelectAgentAsync(
                routingRequest,
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_supervisor.Team.ContainsMember(
            decision.SelectedAgentId))
        {
            throw new InvalidOperationException(
                $"Selected agent '{decision.SelectedAgentId}' " +
                "is no longer registered in the team.");
        }

        if (!_handlers.TryGetValue(
            decision.SelectedAgentId,
            out var handler))
        {
            throw new InvalidOperationException(
                $"No task handler is registered " +
                $"for agent '{decision.SelectedAgentId}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var handlerResult = await handler
            .ExecuteAsync(
                request,
                cancellationToken)
            .ConfigureAwait(false);

        if (handlerResult is null)
        {
            throw new InvalidOperationException(
                $"Handler for agent '{decision.SelectedAgentId}' " +
                "returned a null result.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var completedAtUtc = DateTimeOffset.UtcNow;

        return new AgentDelegationResult(
            routingDecision: decision,
            succeeded: handlerResult.Succeeded,
            output: handlerResult.Output,
            message: handlerResult.Message,
            createdAtUtc: startedAtUtc,
            completedAtUtc: completedAtUtc);
    }
}