namespace EnterpriseDocumentActionAgent.Domain.Agents;

/// <summary>
/// Represents the lifecycle state of an agent.
/// </summary>
public enum AgentStatus
{
    Enabled = 1,
    Disabled = 2
}

/// <summary>
/// Defines execution limits for an agent.
/// </summary>
/// <remarks>
/// These limits are configuration constraints.
///
/// The application execution loop is responsible
/// for enforcing them during agent execution.
/// </remarks>
public sealed record AgentExecutionLimits
{
    public const int MaximumAllowedReasoningSteps = 100;

    public const int MaximumAllowedToolExecutions = 100;

    public int MaximumReasoningSteps { get; init; } = 10;

    public int MaximumToolExecutions { get; init; } = 5;

    public void Validate()
    {
        if (MaximumReasoningSteps <= 0 ||
            MaximumReasoningSteps > MaximumAllowedReasoningSteps)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumReasoningSteps),
                $"Maximum reasoning steps must be between 1 " +
                $"and {MaximumAllowedReasoningSteps}.");
        }

        if (MaximumToolExecutions < 0 ||
            MaximumToolExecutions > MaximumAllowedToolExecutions)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumToolExecutions),
                $"Maximum tool executions must be between 0 " +
                $"and {MaximumAllowedToolExecutions}.");
        }
    }
}

/// <summary>
/// Represents a tenant-owned agent configuration.
/// </summary>
/// <remarks>
/// This aggregate stores agent configuration only.
///
/// It does not execute tools, call language models,
/// authorize requests, or perform workflow actions.
///
/// Tenant authorization must be enforced by the
/// application boundary before this aggregate is used.
/// </remarks>
public sealed class Agent
{
    public Guid Id { get; private set; }

    public string TenantId { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public AgentStatus Status { get; private set; }

    public AgentExecutionLimits ExecutionLimits { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private Agent(
        Guid id,
        string tenantId,
        string name,
        string description,
        AgentExecutionLimits executionLimits,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        Name = name;
        Description = description;
        ExecutionLimits = executionLimits;

        Status = AgentStatus.Disabled;

        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Creates a disabled agent configuration.
    /// </summary>
    /// <remarks>
    /// Agents are disabled by default to prevent
    /// accidental execution before their safety
    /// and infrastructure dependencies are configured.
    /// </remarks>
    public static Agent Create(
        string tenantId,
        string name,
        string description,
        AgentExecutionLimits? executionLimits = null,
        DateTimeOffset? createdAtUtc = null)
    {
        var validatedTenantId = ValidateRequiredText(
            tenantId,
            nameof(tenantId),
            maximumLength: 200);

        var validatedName = ValidateRequiredText(
            name,
            nameof(name),
            maximumLength: 200);

        var validatedDescription = ValidateOptionalText(
            description,
            nameof(description),
            maximumLength: 2000);

        var limits = executionLimits
            ?? new AgentExecutionLimits();

        limits.Validate();

        var now = createdAtUtc ?? DateTimeOffset.UtcNow;

        return new Agent(
            id: Guid.NewGuid(),
            tenantId: validatedTenantId,
            name: validatedName,
            description: validatedDescription,
            executionLimits: limits,
            createdAtUtc: now.ToUniversalTime());
    }

    /// <summary>
    /// Updates the agent's descriptive metadata.
    /// </summary>
    public void UpdateMetadata(
        string name,
        string description)
    {
        Name = ValidateRequiredText(
            name,
            nameof(name),
            maximumLength: 200);

        Description = ValidateOptionalText(
            description,
            nameof(description),
            maximumLength: 2000);

        Touch();
    }

    /// <summary>
    /// Updates the agent's execution limits.
    /// </summary>
    public void UpdateExecutionLimits(
        AgentExecutionLimits executionLimits)
    {
        ArgumentNullException.ThrowIfNull(
            executionLimits);

        executionLimits.Validate();

        ExecutionLimits = executionLimits;

        Touch();
    }

    /// <summary>
    /// Enables the agent configuration.
    /// </summary>
    /// <remarks>
    /// Enabling an agent does not bypass authorization,
    /// approval requirements, tool allow-lists,
    /// or execution safety policies.
    /// </remarks>
    public void Enable()
    {
        if (Status == AgentStatus.Enabled)
        {
            return;
        }

        ExecutionLimits.Validate();

        Status = AgentStatus.Enabled;

        Touch();
    }

    /// <summary>
    /// Disables the agent configuration.
    /// </summary>
    public void Disable()
    {
        if (Status == AgentStatus.Disabled)
        {
            return;
        }

        Status = AgentStatus.Disabled;

        Touch();
    }

    /// <summary>
    /// Verifies that the agent belongs to the
    /// expected tenant.
    /// </summary>
    public void EnsureTenant(
        string tenantId)
    {
        var validatedTenantId = ValidateRequiredText(
            tenantId,
            nameof(tenantId),
            maximumLength: 200);

        if (!string.Equals(
            TenantId,
            validatedTenantId,
            StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Agent does not belong to the " +
                "requested tenant.");
        }
    }

    /// <summary>
    /// Ensures that the agent is enabled.
    /// </summary>
    public void EnsureEnabled()
    {
        if (Status != AgentStatus.Enabled)
        {
            throw new InvalidOperationException(
                "Agent is disabled.");
        }
    }

    private void Touch()
    {
        var now = DateTimeOffset.UtcNow;

        UpdatedAtUtc = now > UpdatedAtUtc
            ? now
            : UpdatedAtUtc.AddTicks(1);
    }

    private static string ValidateRequiredText(
        string value,
        string parameterName,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Value cannot be empty.",
                parameterName);
        }

        var normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static string ValidateOptionalText(
        string? value,
        string parameterName,
        int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"Value cannot exceed {maximumLength} characters.",
                parameterName);
        }

        return normalized;
    }
}