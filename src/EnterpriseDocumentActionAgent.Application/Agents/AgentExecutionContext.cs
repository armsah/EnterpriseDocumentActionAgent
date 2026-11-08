using EnterpriseDocumentActionAgent.Domain.Agents;

namespace EnterpriseDocumentActionAgent.Application.Agents;

/// <summary>
/// Represents a point-in-time snapshot of
/// an agent execution context.
/// </summary>
public sealed record AgentExecutionSnapshot(
    Guid ExecutionId,
    Guid AgentId,
    string TenantId,
    string SubjectId,
    string UserRequest,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? DeadlineUtc,
    int MaximumReasoningSteps,
    int MaximumToolExecutions,
    int ReasoningSteps,
    int ToolExecutions,
    IReadOnlyList<AgentReasoningToolObservation> ToolObservations);

/// <summary>
/// Represents an execution context validation failure.
/// </summary>
public sealed class AgentExecutionContextException
    : Exception
{
    public AgentExecutionContextException(
        string message)
        : base(message)
    {
    }
}

/// <summary>
/// Maintains execution-scoped state for a single agent run.
/// </summary>
/// <remarks>
/// Each context instance represents one execution.
///
/// The context enforces configured reasoning-step
/// and tool-execution limits.
///
/// It does not authenticate users, authorize tools,
/// approve actions, or invoke language models.
///
/// Instances are not designed for concurrent mutation.
/// Create one context per execution.
/// </remarks>
public sealed class AgentExecutionContext
{
    private readonly List<AgentReasoningToolObservation>
        _toolObservations = new();

    private int _reasoningSteps;

    private int _toolExecutions;

    public Guid ExecutionId { get; }

    public Guid AgentId { get; }

    public string TenantId { get; }

    public string SubjectId { get; }

    public string UserRequest { get; }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset? DeadlineUtc { get; }

    public int MaximumReasoningSteps { get; }

    public int MaximumToolExecutions { get; }

    public int ReasoningSteps => _reasoningSteps;

    public int ToolExecutions => _toolExecutions;

    public bool HasRemainingReasoningSteps =>
        _reasoningSteps < MaximumReasoningSteps;

    public bool HasRemainingToolExecutions =>
        _toolExecutions < MaximumToolExecutions;

    /// <summary>
    /// Returns a snapshot of recorded tool observations.
    /// </summary>
    public IReadOnlyList<AgentReasoningToolObservation>
        ToolObservations => _toolObservations.ToArray();

    private AgentExecutionContext(
        Guid executionId,
        Guid agentId,
        string tenantId,
        string subjectId,
        string userRequest,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? deadlineUtc,
        AgentExecutionLimits limits)
    {
        ExecutionId = executionId;
        AgentId = agentId;
        TenantId = tenantId;
        SubjectId = subjectId;
        UserRequest = userRequest;
        StartedAtUtc = startedAtUtc;
        DeadlineUtc = deadlineUtc;

        MaximumReasoningSteps =
            limits.MaximumReasoningSteps;

        MaximumToolExecutions =
            limits.MaximumToolExecutions;
    }

    /// <summary>
    /// Creates an execution context for an enabled,
    /// tenant-owned agent.
    /// </summary>
    public static AgentExecutionContext Create(
        Agent agent,
        string tenantId,
        string subjectId,
        string userRequest,
        TimeSpan? maximumDuration = null,
        DateTimeOffset? startedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(agent);

        agent.EnsureTenant(tenantId);

        agent.EnsureEnabled();

        ValidateRequiredText(
            subjectId,
            nameof(subjectId),
            maximumLength: 200);

        ValidateRequiredText(
            userRequest,
            nameof(userRequest),
            AgentReasoningValidation
                .MaximumUserRequestCharacters);

        var limits = agent.ExecutionLimits;

        limits.Validate();

        if (maximumDuration.HasValue &&
            (maximumDuration.Value <= TimeSpan.Zero ||
             maximumDuration.Value > TimeSpan.FromHours(1)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDuration),
                "Maximum execution duration must be " +
                "greater than zero and no more than one hour.");
        }

        var start = (
            startedAtUtc ?? DateTimeOffset.UtcNow
        ).ToUniversalTime();

        DateTimeOffset? deadline = null;

        if (maximumDuration.HasValue)
        {
            deadline = start.Add(maximumDuration.Value);
        }

        return new AgentExecutionContext(
            executionId: Guid.NewGuid(),
            agentId: agent.Id,
            tenantId: tenantId.Trim(),
            subjectId: subjectId.Trim(),
            userRequest: userRequest,
            startedAtUtc: start,
            deadlineUtc: deadline,
            limits: limits);
    }

    /// <summary>
    /// Ensures the execution has not exceeded its deadline.
    /// </summary>
    public void EnsureWithinDeadline(
        DateTimeOffset? currentTimeUtc = null)
    {
        if (!DeadlineUtc.HasValue)
        {
            return;
        }

        var now = (
            currentTimeUtc ?? DateTimeOffset.UtcNow
        ).ToUniversalTime();

        if (now >= DeadlineUtc.Value)
        {
            throw new AgentExecutionContextException(
                "Agent execution deadline has been reached.");
        }
    }

    /// <summary>
    /// Records the beginning of a reasoning step.
    /// </summary>
    /// <remarks>
    /// The counter is incremented before the reasoning
    /// provider is invoked.
    /// </remarks>
    public int BeginReasoningStep(
        DateTimeOffset? currentTimeUtc = null)
    {
        EnsureWithinDeadline(currentTimeUtc);

        if (!HasRemainingReasoningSteps)
        {
            throw new AgentExecutionContextException(
                "Agent reasoning-step limit has been reached.");
        }

        _reasoningSteps++;

        return _reasoningSteps;
    }

    /// <summary>
    /// Records the beginning of a tool execution.
    /// </summary>
    /// <remarks>
    /// The counter is incremented before the tool
    /// executor is invoked.
    ///
    /// Authorization and approval checks remain
    /// responsibilities of the execution layer.
    /// </remarks>
    public int BeginToolExecution(
        DateTimeOffset? currentTimeUtc = null)
    {
        EnsureWithinDeadline(currentTimeUtc);

        if (!HasRemainingToolExecutions)
        {
            throw new AgentExecutionContextException(
                "Agent tool-execution limit has been reached.");
        }

        _toolExecutions++;

        return _toolExecutions;
    }

    /// <summary>
    /// Records an observation returned by a tool.
    /// </summary>
    /// <remarks>
    /// Tool observations are untrusted data.
    ///
    /// This method validates their structure and size
    /// but does not determine whether the content
    /// contains malicious instructions.
    /// </remarks>
    public void RecordToolObservation(
        AgentReasoningToolObservation observation,
        DateTimeOffset? currentTimeUtc = null)
    {
        ArgumentNullException.ThrowIfNull(observation);

        EnsureWithinDeadline(currentTimeUtc);

        ValidateRequiredText(
            observation.ToolName,
            nameof(observation.ToolName),
            AgentReasoningValidation
                .MaximumToolNameCharacters);

        if (observation.Output is not null &&
            observation.Output.Length >
                AgentReasoningValidation
                    .MaximumToolObservationCharacters)
        {
            throw new AgentExecutionContextException(
                "Tool observation output exceeds " +
                "the maximum permitted length.");
        }

        if (_toolObservations.Count >=
            AgentReasoningValidation.MaximumToolObservations)
        {
            throw new AgentExecutionContextException(
                "Maximum tool observation count has been reached.");
        }

        if (_toolObservations.Count >= _toolExecutions)
        {
            throw new AgentExecutionContextException(
                "Cannot record more tool observations " +
                "than started tool executions.");
        }

        _toolObservations.Add(observation);
    }

    /// <summary>
    /// Creates an immutable snapshot of the current
    /// execution state.
    /// </summary>
    public AgentExecutionSnapshot CreateSnapshot()
    {
        return new AgentExecutionSnapshot(
            ExecutionId: ExecutionId,
            AgentId: AgentId,
            TenantId: TenantId,
            SubjectId: SubjectId,
            UserRequest: UserRequest,
            StartedAtUtc: StartedAtUtc,
            DeadlineUtc: DeadlineUtc,
            MaximumReasoningSteps: MaximumReasoningSteps,
            MaximumToolExecutions: MaximumToolExecutions,
            ReasoningSteps: _reasoningSteps,
            ToolExecutions: _toolExecutions,
            ToolObservations: _toolObservations.ToArray());
    }

    /// <summary>
    /// Creates a reasoning request using the current
    /// execution state.
    /// </summary>
    /// <remarks>
    /// Call BeginReasoningStep before creating a
    /// request for that step.
    /// </remarks>
    public AgentReasoningRequest CreateReasoningRequest(
        int maximumResponseCharacters)
    {
        if (_reasoningSteps <= 0)
        {
            throw new AgentExecutionContextException(
                "A reasoning step must be started first.");
        }

        var request = new AgentReasoningRequest(
            AgentId: AgentId,
            ExecutionId: ExecutionId,
            TenantId: TenantId,
            SubjectId: SubjectId,
            UserRequest: UserRequest,
            StepNumber: _reasoningSteps,
            MaximumResponseCharacters:
                maximumResponseCharacters,
            ToolObservations: _toolObservations.ToArray());

        AgentReasoningValidation.ValidateRequest(
            request);

        return request;
    }

    private static void ValidateRequiredText(
        string? value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Value cannot be empty.",
                parameterName);
        }

        if (value.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Value cannot exceed {maximumLength} characters.",
                parameterName);
        }
    }
}