using EnterpriseDocumentActionAgent.Application.AI.Providers;
using EnterpriseDocumentActionAgent.Application.AI.Runtime;

namespace EnterpriseDocumentActionAgent.Application.AI.Templates;

/// <summary>
/// Represents optional overrides applied when
/// creating an agent configuration from a template.
/// </summary>
public sealed record AgentTemplateOverrides
{
    /// <summary>
    /// Overrides the agent identifier.
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// Overrides the model identifier.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Overrides the provider identifier.
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// Overrides the system instructions.
    /// </summary>
    public string? SystemInstructions { get; init; }

    /// <summary>
    /// Overrides the temperature.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Overrides the maximum output tokens.
    /// </summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>
    /// Overrides the maximum execution steps.
    /// </summary>
    public int? MaxExecutionSteps { get; init; }

    /// <summary>
    /// Overrides the execution timeout.
    /// </summary>
    public TimeSpan? ExecutionTimeout { get; init; }

    /// <summary>
    /// Replaces the required model capabilities.
    /// </summary>
    public IReadOnlyCollection<AiModelCapability>?
        RequiredCapabilities
    { get; init; }
}

/// <summary>
/// Defines an immutable, reusable AI agent
/// configuration template.
/// </summary>
/// <remarks>
/// A template is a configuration definition.
///
/// It does not instantiate model providers,
/// create files, configure infrastructure,
/// or execute agents.
///
/// The CreateConfiguration method produces
/// a new AgentConfiguration using the
/// template defaults and optional overrides.
/// </remarks>
public sealed class AgentProjectTemplate
{
    /// <summary>
    /// Default execution timeout.
    /// </summary>
    public static readonly TimeSpan DefaultExecutionTimeout =
        TimeSpan.FromMinutes(2);

    private readonly AiModelCapability[] _requiredCapabilities;

    /// <summary>
    /// Gets the unique template identifier.
    /// </summary>
    public string TemplateId { get; }

    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the template description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the default model identifier.
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// Gets the optional provider identifier.
    /// </summary>
    public string? ProviderId { get; }

    /// <summary>
    /// Gets the default system instructions.
    /// </summary>
    public string SystemInstructions { get; }

    /// <summary>
    /// Gets the default temperature.
    /// </summary>
    public double Temperature { get; }

    /// <summary>
    /// Gets the maximum output tokens.
    /// </summary>
    public int MaxOutputTokens { get; }

    /// <summary>
    /// Gets the maximum execution steps.
    /// </summary>
    public int MaxExecutionSteps { get; }

    /// <summary>
    /// Gets the execution timeout.
    /// </summary>
    public TimeSpan ExecutionTimeout { get; }

    /// <summary>
    /// Gets the required model capabilities.
    /// </summary>
    public IReadOnlyList<AiModelCapability>
        RequiredCapabilities =>
            Array.AsReadOnly(_requiredCapabilities);

    /// <summary>
    /// Initializes a reusable agent template.
    /// </summary>
    public AgentProjectTemplate(
        string templateId,
        string name,
        string description,
        string model,
        string systemInstructions,
        string? providerId = null,
        double temperature = 0.7,
        int maxOutputTokens = 2048,
        int maxExecutionSteps = 10,
        TimeSpan? executionTimeout = null,
        IEnumerable<AiModelCapability>?
            requiredCapabilities = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            templateId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            name);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            model);

        ArgumentNullException.ThrowIfNull(
            description);

        ArgumentNullException.ThrowIfNull(
            systemInstructions);

        ValidateTemperature(temperature);

        ValidatePositive(
            maxOutputTokens,
            nameof(maxOutputTokens));

        ValidatePositive(
            maxExecutionSteps,
            nameof(maxExecutionSteps));

        var resolvedTimeout =
            executionTimeout ?? DefaultExecutionTimeout;

        ValidateTimeout(resolvedTimeout);

        TemplateId = templateId.Trim();

        Name = name.Trim();

        Description = description.Trim();

        Model = model.Trim();

        ProviderId =
            string.IsNullOrWhiteSpace(providerId)
                ? null
                : providerId.Trim();

        SystemInstructions = systemInstructions.Trim();

        Temperature = temperature;

        MaxOutputTokens = maxOutputTokens;

        MaxExecutionSteps = maxExecutionSteps;

        ExecutionTimeout = resolvedTimeout;

        _requiredCapabilities =
            NormalizeCapabilities(requiredCapabilities);
    }

    /// <summary>
    /// Creates an agent configuration from
    /// this template.
    /// </summary>
    /// <param name="agentId">
    /// Identifier assigned to the created agent.
    /// </param>
    /// <param name="overrides">
    /// Optional configuration overrides.
    /// </param>
    public AgentConfiguration CreateConfiguration(
        string agentId,
        AgentTemplateOverrides? overrides = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            agentId);

        var resolvedAgentId =
            overrides?.AgentId ?? agentId;

        var resolvedModel =
            overrides?.Model ?? Model;

        var resolvedProviderId =
            overrides?.ProviderId ?? ProviderId;

        var resolvedInstructions =
            overrides?.SystemInstructions
            ?? SystemInstructions;

        var resolvedTemperature =
            overrides?.Temperature ?? Temperature;

        var resolvedMaxOutputTokens =
            overrides?.MaxOutputTokens
            ?? MaxOutputTokens;

        var resolvedMaxExecutionSteps =
            overrides?.MaxExecutionSteps
            ?? MaxExecutionSteps;

        var resolvedTimeout =
            overrides?.ExecutionTimeout
            ?? ExecutionTimeout;

        var resolvedCapabilities =
            overrides?.RequiredCapabilities
            ?? RequiredCapabilities;

        ArgumentException.ThrowIfNullOrWhiteSpace(
            resolvedAgentId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            resolvedModel);

        ArgumentNullException.ThrowIfNull(
            resolvedInstructions);

        ValidateTemperature(
            resolvedTemperature);

        ValidatePositive(
            resolvedMaxOutputTokens,
            nameof(AgentTemplateOverrides.MaxOutputTokens));

        ValidatePositive(
            resolvedMaxExecutionSteps,
            nameof(AgentTemplateOverrides.MaxExecutionSteps));

        ValidateTimeout(
            resolvedTimeout);

        return new AgentConfiguration(
            agentId: resolvedAgentId.Trim(),
            model: resolvedModel.Trim(),
            systemInstructions:
                resolvedInstructions.Trim(),
            providerId: resolvedProviderId,
            temperature: resolvedTemperature,
            maxOutputTokens:
                resolvedMaxOutputTokens,
            maxExecutionSteps:
                resolvedMaxExecutionSteps,
            executionTimeout: resolvedTimeout,
            requiredCapabilities:
                NormalizeCapabilities(
                    resolvedCapabilities));
    }

    /// <summary>
    /// Creates a copy of this template with
    /// a different model.
    /// </summary>
    public AgentProjectTemplate WithModel(
        string model,
        string? providerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            model);

        return new AgentProjectTemplate(
            templateId: TemplateId,
            name: Name,
            description: Description,
            model: model,
            systemInstructions: SystemInstructions,
            providerId: providerId ?? ProviderId,
            temperature: Temperature,
            maxOutputTokens: MaxOutputTokens,
            maxExecutionSteps: MaxExecutionSteps,
            executionTimeout: ExecutionTimeout,
            requiredCapabilities:
                _requiredCapabilities);
    }

    /// <summary>
    /// Creates a copy with different
    /// system instructions.
    /// </summary>
    public AgentProjectTemplate WithInstructions(
        string systemInstructions)
    {
        ArgumentNullException.ThrowIfNull(
            systemInstructions);

        return new AgentProjectTemplate(
            templateId: TemplateId,
            name: Name,
            description: Description,
            model: Model,
            systemInstructions: systemInstructions,
            providerId: ProviderId,
            temperature: Temperature,
            maxOutputTokens: MaxOutputTokens,
            maxExecutionSteps: MaxExecutionSteps,
            executionTimeout: ExecutionTimeout,
            requiredCapabilities:
                _requiredCapabilities);
    }

    /// <summary>
    /// Creates a copy with different
    /// required capabilities.
    /// </summary>
    public AgentProjectTemplate WithCapabilities(
        IEnumerable<AiModelCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(
            capabilities);

        return new AgentProjectTemplate(
            templateId: TemplateId,
            name: Name,
            description: Description,
            model: Model,
            systemInstructions: SystemInstructions,
            providerId: ProviderId,
            temperature: Temperature,
            maxOutputTokens: MaxOutputTokens,
            maxExecutionSteps: MaxExecutionSteps,
            executionTimeout: ExecutionTimeout,
            requiredCapabilities: capabilities);
    }

    /// <summary>
    /// Creates a template for document analysis.
    /// </summary>
    public static AgentProjectTemplate DocumentAnalysis(
        string model,
        string? providerId = null)
    {
        return new AgentProjectTemplate(
            templateId: "document-analysis",
            name: "Document Analysis Agent",
            description:
                "Analyzes documents and produces " +
                "structured, evidence-based summaries.",
            model: model,
            systemInstructions:
                "Analyze the supplied document carefully. " +
                "Distinguish facts from interpretations. " +
                "Do not invent missing information. " +
                "Report uncertainty when appropriate.",
            providerId: providerId,
            temperature: 0.2,
            maxOutputTokens: 4096,
            maxExecutionSteps: 10,
            requiredCapabilities:
            [
                AiModelCapability.TextGeneration
            ]);
    }

    /// <summary>
    /// Creates a template for tool-assisted agents.
    /// </summary>
    public static AgentProjectTemplate ToolAssistant(
        string model,
        string? providerId = null)
    {
        return new AgentProjectTemplate(
            templateId: "tool-assistant",
            name: "Tool Assistant Agent",
            description:
                "Assists with tasks using " +
                "explicitly authorized tools.",
            model: model,
            systemInstructions:
                "Use tools only when they are " +
                "explicitly authorized. " +
                "Do not claim that an action " +
                "was completed unless its result " +
                "has been verified.",
            providerId: providerId,
            temperature: 0.4,
            maxOutputTokens: 2048,
            maxExecutionSteps: 15,
            requiredCapabilities:
            [
                AiModelCapability.TextGeneration,
                AiModelCapability.ToolCalling
            ]);
    }

    /// <summary>
    /// Creates a template for structured output.
    /// </summary>
    public static AgentProjectTemplate StructuredExtraction(
        string model,
        string? providerId = null)
    {
        return new AgentProjectTemplate(
            templateId: "structured-extraction",
            name: "Structured Extraction Agent",
            description:
                "Extracts structured information " +
                "from supplied content.",
            model: model,
            systemInstructions:
                "Extract only information supported " +
                "by the supplied content. " +
                "Return valid structured output. " +
                "Do not fabricate missing values.",
            providerId: providerId,
            temperature: 0,
            maxOutputTokens: 2048,
            maxExecutionSteps: 10,
            requiredCapabilities:
            [
                AiModelCapability.TextGeneration,
                AiModelCapability.StructuredOutput
            ]);
    }

    /// <summary>
    /// Returns a readable template summary.
    /// </summary>
    public override string ToString()
    {
        return
            $"{Name} ({TemplateId}) - " +
            $"Model: {Model}, " +
            $"Provider: {ProviderId ?? "automatic"}, " +
            $"Capabilities: {_requiredCapabilities.Length}";
    }

    private static void ValidateTemperature(
        double temperature)
    {
        if (double.IsNaN(temperature) ||
            double.IsInfinity(temperature) ||
            temperature < 0 ||
            temperature > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(temperature),
                "Temperature must be between 0 and 2.");
        }
    }

    private static void ValidatePositive(
        int value,
        string parameterName)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Value must be positive.");
        }
    }

    private static void ValidateTimeout(
        TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Execution timeout must be positive.");
        }
    }

    private static AiModelCapability[]
        NormalizeCapabilities(
            IEnumerable<AiModelCapability>?
                capabilities)
    {
        if (capabilities is null)
        {
            return [];
        }

        var values = capabilities
            .Distinct()
            .OrderBy(capability => capability)
            .ToArray();

        foreach (var capability in values)
        {
            if (!Enum.IsDefined(capability))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capabilities),
                    $"Unknown model capability: {capability}.");
            }
        }

        return values;
    }
}