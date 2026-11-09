using System.Collections.Frozen;
using EnterpriseDocumentActionAgent.Application.AI.Providers;

namespace EnterpriseDocumentActionAgent.Application.AI.Runtime;

/// <summary>
/// Defines immutable configuration for an AI agent.
/// </summary>
/// <remarks>
/// This contract describes agent identity, model selection,
/// generation parameters, execution limits, and required
/// model capabilities.
///
/// Runtime components are responsible for enforcing
/// these settings during agent execution.
/// </remarks>
public sealed record AgentConfiguration
{
    public string AgentId { get; }

    public string Model { get; }

    public string? ProviderId { get; }

    public string SystemInstructions { get; }

    public double Temperature { get; }

    public int MaxOutputTokens { get; }

    public int MaxExecutionSteps { get; }

    public TimeSpan ExecutionTimeout { get; }

    public IReadOnlySet<AiModelCapability> RequiredCapabilities { get; }

    public AgentConfiguration(
        string agentId,
        string model,
        string systemInstructions,
        string? providerId = null,
        double temperature = 0.7,
        int maxOutputTokens = 2048,
        int maxExecutionSteps = 10,
        TimeSpan? executionTimeout = null,
        IEnumerable<AiModelCapability>? requiredCapabilities = null)
    {
        ValidateIdentifier(agentId, nameof(agentId));

        ValidateIdentifier(model, nameof(model));

        if (providerId is not null)
        {
            ValidateIdentifier(providerId, nameof(providerId));
        }

        ArgumentNullException.ThrowIfNull(systemInstructions);

        if (systemInstructions.Length > 100_000)
        {
            throw new ArgumentException(
                "System instructions exceed the maximum length.",
                nameof(systemInstructions));
        }

        if (double.IsNaN(temperature) ||
            double.IsInfinity(temperature) ||
            temperature < 0 ||
            temperature > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(temperature),
                "Temperature must be between 0 and 2.");
        }

        if (maxOutputTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxOutputTokens),
                "Maximum output tokens must be positive.");
        }

        if (maxExecutionSteps <= 0 || maxExecutionSteps > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExecutionSteps),
                "Execution steps must be between 1 and 1000.");
        }

        var timeout = executionTimeout
            ?? TimeSpan.FromMinutes(5);

        if (timeout <= TimeSpan.Zero ||
            timeout > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(
                nameof(executionTimeout),
                "Execution timeout must be greater than zero " +
                "and cannot exceed 24 hours.");
        }

        var capabilities = new HashSet<AiModelCapability>();

        if (requiredCapabilities is not null)
        {
            foreach (var capability in requiredCapabilities)
            {
                if (!Enum.IsDefined(capability))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(requiredCapabilities),
                        capability,
                        "Capability value is not recognized.");
                }

                capabilities.Add(capability);
            }
        }

        AgentId = agentId;

        Model = model;

        ProviderId = providerId;

        SystemInstructions = systemInstructions;

        Temperature = temperature;

        MaxOutputTokens = maxOutputTokens;

        MaxExecutionSteps = maxExecutionSteps;

        ExecutionTimeout = timeout;

        RequiredCapabilities = capabilities.ToFrozenSet();
    }

    /// <summary>
    /// Determines whether the configuration requires
    /// a particular model capability.
    /// </summary>
    public bool RequiresCapability(AiModelCapability capability)
    {
        if (!Enum.IsDefined(capability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(capability));
        }

        return RequiredCapabilities.Contains(capability);
    }

    /// <summary>
    /// Validates the configured model capabilities
    /// against a registered capability descriptor.
    /// </summary>
    public void ValidateModelCapabilities(
        AiModelCapabilities modelCapabilities)
    {
        ArgumentNullException.ThrowIfNull(modelCapabilities);

        if (!string.Equals(
                Model,
                modelCapabilities.Model,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Capability descriptor does not match " +
                "the configured model.",
                nameof(modelCapabilities));
        }

        modelCapabilities.Require(RequiredCapabilities);
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