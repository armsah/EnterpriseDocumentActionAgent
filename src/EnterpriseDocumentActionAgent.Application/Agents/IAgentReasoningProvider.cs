namespace EnterpriseDocumentActionAgent.Application.Agents;

/// <summary>
/// Represents a tool execution observation
/// supplied to an agent reasoning provider.
/// </summary>
/// <remarks>
/// Tool output must be treated as untrusted content.
///
/// Callers must apply appropriate output-size limits,
/// authorization, and prompt-injection defenses
/// before forwarding observations to a model.
/// </remarks>
public sealed record AgentReasoningToolObservation(
    string ToolName,
    bool Succeeded,
    string? Output);

/// <summary>
/// Represents a request for one agent reasoning step.
/// </summary>
public sealed record AgentReasoningRequest(
    Guid AgentId,
    Guid ExecutionId,
    string TenantId,
    string SubjectId,
    string UserRequest,
    int StepNumber,
    int MaximumResponseCharacters,
    IReadOnlyList<AgentReasoningToolObservation> ToolObservations);

/// <summary>
/// Represents the kind of response returned
/// by an agent reasoning provider.
/// </summary>
public enum AgentReasoningResponseKind
{
    FinalAnswer = 1,
    ToolProposal = 2,
    Continue = 3,
    Refusal = 4
}

/// <summary>
/// Represents a structured response from
/// an agent reasoning provider.
/// </summary>
/// <remarks>
/// A tool proposal is not an authorization decision.
///
/// The execution layer must independently enforce
/// tool allow-lists, permissions, approval policies,
/// and tenant isolation.
/// </remarks>
public sealed record AgentReasoningResponse(
    AgentReasoningResponseKind Kind,
    string? FinalAnswer = null,
    string? ToolName = null,
    string? ToolInput = null);

/// <summary>
/// Represents a reasoning provider failure.
/// </summary>
public sealed class AgentReasoningProviderException
    : Exception
{
    public AgentReasoningProviderException(
        string message)
        : base(message)
    {
    }

    public AgentReasoningProviderException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Defines the application contract for
/// agent reasoning providers.
/// </summary>
/// <remarks>
/// Implementations may use hosted language models
/// or deterministic test doubles.
///
/// Providers must not execute tools directly.
///
/// Providers must not trust tool observations as
/// instructions that override application policy.
///
/// Provider implementations are responsible for
/// validating request and response structures,
/// enforcing their configured output limits,
/// and propagating cancellation.
///
/// Authentication, tenant authorization, tool
/// execution, and human approval remain separate
/// application responsibilities.
/// </remarks>
public interface IAgentReasoningProvider
{
    /// <summary>
    /// Produces one structured agent reasoning response.
    /// </summary>
    Task<AgentReasoningResponse> ReasonAsync(
        AgentReasoningRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates reasoning requests and responses
/// at the application boundary.
/// </summary>
public static class AgentReasoningValidation
{
    /// <summary>
    /// Maximum permitted user request length.
    /// </summary>
    public const int MaximumUserRequestCharacters = 32_000;

    /// <summary>
    /// Maximum permitted model response length.
    /// </summary>
    public const int MaximumAllowedResponseCharacters = 32_000;

    /// <summary>
    /// Maximum number of tool observations
    /// supplied to one reasoning step.
    /// </summary>
    public const int MaximumToolObservations = 100;

    /// <summary>
    /// Maximum permitted tool observation output length.
    /// </summary>
    public const int MaximumToolObservationCharacters = 16_000;

    /// <summary>
    /// Maximum permitted tool name length.
    /// </summary>
    public const int MaximumToolNameCharacters = 200;

    /// <summary>
    /// Validates a reasoning request.
    /// </summary>
    public static void ValidateRequest(
        AgentReasoningRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.AgentId == Guid.Empty)
        {
            throw new ArgumentException(
                "Agent identifier is required.",
                nameof(request));
        }

        if (request.ExecutionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution identifier is required.",
                nameof(request));
        }

        ValidateRequiredText(
            request.TenantId,
            nameof(request.TenantId),
            maximumLength: 200);

        ValidateRequiredText(
            request.SubjectId,
            nameof(request.SubjectId),
            maximumLength: 200);

        ValidateRequiredText(
            request.UserRequest,
            nameof(request.UserRequest),
            MaximumUserRequestCharacters);

        if (request.StepNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Step number must be positive.");
        }

        if (request.MaximumResponseCharacters <= 0 ||
            request.MaximumResponseCharacters >
                MaximumAllowedResponseCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Maximum response characters must be " +
                "within the supported range.");
        }

        ArgumentNullException.ThrowIfNull(
            request.ToolObservations);

        if (request.ToolObservations.Count >
            MaximumToolObservations)
        {
            throw new ArgumentException(
                "Too many tool observations.",
                nameof(request));
        }

        foreach (var observation in request.ToolObservations)
        {
            if (observation is null)
            {
                throw new ArgumentException(
                    "Tool observations cannot contain null.",
                    nameof(request));
            }

            ValidateRequiredText(
                observation.ToolName,
                nameof(observation.ToolName),
                MaximumToolNameCharacters);

            if (observation.Output is not null &&
                observation.Output.Length >
                    MaximumToolObservationCharacters)
            {
                throw new ArgumentException(
                    "Tool observation output exceeds " +
                    "the maximum permitted length.",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Validates a reasoning response.
    /// </summary>
    public static void ValidateResponse(
        AgentReasoningResponse response,
        int maximumResponseCharacters)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (maximumResponseCharacters <= 0 ||
            maximumResponseCharacters >
                MaximumAllowedResponseCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumResponseCharacters));
        }

        switch (response.Kind)
        {
            case AgentReasoningResponseKind.FinalAnswer:
                ValidateRequiredText(
                    response.FinalAnswer,
                    nameof(response.FinalAnswer),
                    maximumResponseCharacters);
                break;

            case AgentReasoningResponseKind.ToolProposal:
                ValidateRequiredText(
                    response.ToolName,
                    nameof(response.ToolName),
                    MaximumToolNameCharacters);

                ValidateRequiredText(
                    response.ToolInput,
                    nameof(response.ToolInput),
                    maximumResponseCharacters);
                break;

            case AgentReasoningResponseKind.Continue:
            case AgentReasoningResponseKind.Refusal:
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(response),
                    "Unsupported reasoning response kind.");
        }
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