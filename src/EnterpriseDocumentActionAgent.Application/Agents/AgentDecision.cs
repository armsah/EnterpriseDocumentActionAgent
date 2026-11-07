namespace EnterpriseDocumentActionAgent.Application.Agents;

/// <summary>
/// Represents the type of a normalized agent decision.
/// </summary>
public enum AgentDecisionKind
{
    Complete = 1,
    ProposeTool = 2,
    Continue = 3,
    Reject = 4
}

/// <summary>
/// Represents a validated decision produced by
/// an agent reasoning component.
/// </summary>
/// <remarks>
/// Tool proposals are not authorization decisions.
///
/// A proposed tool must pass through the application's
/// authorization, allow-list, and approval mechanisms
/// before it can be executed.
/// </remarks>
public sealed record AgentDecision
{
    public AgentDecisionKind Kind { get; }

    public string? FinalAnswer { get; }

    public string? ToolName { get; }

    public string? ToolInput { get; }

    private AgentDecision(
        AgentDecisionKind kind,
        string? finalAnswer,
        string? toolName,
        string? toolInput)
    {
        Kind = kind;
        FinalAnswer = finalAnswer;
        ToolName = toolName;
        ToolInput = toolInput;
    }

    /// <summary>
    /// Creates a decision containing a final answer.
    /// </summary>
    public static AgentDecision Complete(
        string finalAnswer)
    {
        ValidateRequiredText(
            finalAnswer,
            nameof(finalAnswer),
            AgentReasoningValidation
                .MaximumAllowedResponseCharacters);

        return new AgentDecision(
            AgentDecisionKind.Complete,
            finalAnswer,
            null,
            null);
    }

    /// <summary>
    /// Creates a proposed tool invocation.
    /// </summary>
    public static AgentDecision ProposeTool(
        string toolName,
        string toolInput)
    {
        ValidateRequiredText(
            toolName,
            nameof(toolName),
            AgentReasoningValidation
                .MaximumToolNameCharacters);

        ValidateRequiredText(
            toolInput,
            nameof(toolInput),
            AgentReasoningValidation
                .MaximumAllowedResponseCharacters);

        return new AgentDecision(
            AgentDecisionKind.ProposeTool,
            null,
            toolName,
            toolInput);
    }

    /// <summary>
    /// Creates a decision requesting another reasoning step.
    /// </summary>
    public static AgentDecision Continue()
    {
        return new AgentDecision(
            AgentDecisionKind.Continue,
            null,
            null,
            null);
    }

    /// <summary>
    /// Creates a decision rejecting the request.
    /// </summary>
    public static AgentDecision Reject()
    {
        return new AgentDecision(
            AgentDecisionKind.Reject,
            null,
            null,
            null);
    }

    /// <summary>
    /// Converts a reasoning-provider response into
    /// a validated agent decision.
    /// </summary>
    public static AgentDecision FromReasoningResponse(
        AgentReasoningResponse response,
        int maximumResponseCharacters)
    {
        AgentReasoningValidation.ValidateResponse(
            response,
            maximumResponseCharacters);

        return response.Kind switch
        {
            AgentReasoningResponseKind.FinalAnswer =>
                Complete(response.FinalAnswer!),

            AgentReasoningResponseKind.ToolProposal =>
                ProposeTool(
                    response.ToolName!,
                    response.ToolInput!),

            AgentReasoningResponseKind.Continue =>
                Continue(),

            AgentReasoningResponseKind.Refusal =>
                Reject(),

            _ => throw new ArgumentOutOfRangeException(
                nameof(response),
                "Unsupported reasoning response kind.")
        };
    }

    /// <summary>
    /// Converts the decision into the existing
    /// execution-loop decision contract.
    /// </summary>
    public AgentLoopDecision ToLoopDecision()
    {
        return Kind switch
        {
            AgentDecisionKind.Complete =>
                new AgentLoopDecision(
                    Kind: AgentLoopDecisionKind.Complete,
                    FinalAnswer: FinalAnswer),

            AgentDecisionKind.ProposeTool =>
                new AgentLoopDecision(
                    Kind: AgentLoopDecisionKind.ExecuteTool,
                    ToolName: ToolName,
                    ToolInput: ToolInput),

            AgentDecisionKind.Continue =>
                new AgentLoopDecision(
                    Kind: AgentLoopDecisionKind.Continue),

            AgentDecisionKind.Reject =>
                new AgentLoopDecision(
                    Kind: AgentLoopDecisionKind.Reject),

            _ => throw new InvalidOperationException(
                "Unsupported agent decision kind.")
        };
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

/// <summary>
/// Configures the reasoning adapter.
/// </summary>
public sealed record AgentReasoningAdapterOptions
{
    public int MaximumResponseCharacters { get; init; } =
        16_000;
}

/// <summary>
/// Adapts the application reasoning provider
/// to the existing agent execution-loop contract.
/// </summary>
/// <remarks>
/// A unique execution identifier is created for each
/// adapter instance.
///
/// For execution-level isolation, callers should create
/// a new adapter instance for each agent execution.
///
/// The adapter does not execute tools or authorize actions.
/// </remarks>
public sealed class AgentReasoningAdapter
    : IAgentLoopReasoner
{
    private readonly IAgentReasoningProvider _provider;

    private readonly AgentReasoningAdapterOptions _options;

    private readonly Guid _executionId;

    public AgentReasoningAdapter(
        IAgentReasoningProvider provider,
        AgentReasoningAdapterOptions? options = null)
    {
        _provider = provider
            ?? throw new ArgumentNullException(
                nameof(provider));

        _options = options
            ?? new AgentReasoningAdapterOptions();

        if (_options.MaximumResponseCharacters <= 0 ||
            _options.MaximumResponseCharacters >
                AgentReasoningValidation
                    .MaximumAllowedResponseCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum response characters must be " +
                "within the supported range.");
        }

        _executionId = Guid.NewGuid();
    }

    /// <summary>
    /// Produces one execution-loop decision.
    /// </summary>
    public async Task<AgentLoopDecision> DecideAsync(
        AgentLoopStepRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(
            request.ToolResults);

        var observations = request.ToolResults
            .Select(result => new AgentReasoningToolObservation(
                ToolName: result.ToolName,
                Succeeded: result.Succeeded,
                Output: result.Output))
            .ToArray();

        var reasoningRequest = new AgentReasoningRequest(
            AgentId: request.AgentId,
            ExecutionId: _executionId,
            TenantId: request.TenantId,
            SubjectId: request.SubjectId,
            UserRequest: request.UserRequest,
            StepNumber: request.StepNumber,
            MaximumResponseCharacters:
                _options.MaximumResponseCharacters,
            ToolObservations: observations);

        AgentReasoningValidation.ValidateRequest(
            reasoningRequest);

        var response = await _provider.ReasonAsync(
            reasoningRequest,
            cancellationToken);

        var decision = AgentDecision.FromReasoningResponse(
            response,
            _options.MaximumResponseCharacters);

        return decision.ToLoopDecision();
    }
}