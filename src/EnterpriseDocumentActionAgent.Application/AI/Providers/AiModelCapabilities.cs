using System.Collections.Frozen;

namespace EnterpriseDocumentActionAgent.Application.AI.Providers;

/// <summary>
/// Features that an AI model may support.
/// </summary>
public enum AiModelCapability
{
    TextGeneration = 1,
    ImageInput = 2,
    AudioInput = 3,
    AudioOutput = 4,
    VideoInput = 5,
    ToolCalling = 6,
    StructuredOutput = 7,
    Streaming = 8,
    Embeddings = 9
}

/// <summary>
/// Immutable description of a model's supported capabilities.
/// </summary>
public sealed record AiModelCapabilities
{
    public string Model { get; }

    public IReadOnlySet<AiModelCapability> Supported { get; }

    public AiModelCapabilities(
        string model,
        IEnumerable<AiModelCapability> capabilities)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException(
                "Model identifier is required.",
                nameof(model));
        }

        if (model.Length > 200)
        {
            throw new ArgumentException(
                "Model identifier cannot exceed 200 characters.",
                nameof(model));
        }

        ArgumentNullException.ThrowIfNull(capabilities);

        var supported = new HashSet<AiModelCapability>();

        foreach (var capability in capabilities)
        {
            if (!Enum.IsDefined(capability))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capabilities),
                    $"Unsupported capability value: {capability}.");
            }

            supported.Add(capability);
        }

        Supported = supported.ToFrozenSet();

        Model = model;
    }

    /// <summary>
    /// Determines whether one capability is supported.
    /// </summary>
    public bool Supports(AiModelCapability capability)
    {
        ValidateCapability(capability);

        return Supported.Contains(capability);
    }

    /// <summary>
    /// Determines whether every requested capability
    /// is supported by this model.
    /// </summary>
    public bool SupportsAll(
        IEnumerable<AiModelCapability> required)
    {
        ArgumentNullException.ThrowIfNull(required);

        foreach (var capability in required)
        {
            ValidateCapability(capability);

            if (!Supported.Contains(capability))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the requested capabilities that
    /// this model does not support.
    /// </summary>
    public IReadOnlyList<AiModelCapability> GetMissing(
        IEnumerable<AiModelCapability> required)
    {
        ArgumentNullException.ThrowIfNull(required);

        var missing = new HashSet<AiModelCapability>();

        foreach (var capability in required)
        {
            ValidateCapability(capability);

            if (!Supported.Contains(capability))
            {
                missing.Add(capability);
            }
        }

        return missing.OrderBy(
            capability => capability).ToArray();
    }

    /// <summary>
    /// Ensures that all required capabilities
    /// are available before model invocation.
    /// </summary>
    public void Require(
        IEnumerable<AiModelCapability> required)
    {
        var missing = GetMissing(required);

        if (missing.Count == 0)
        {
            return;
        }

        throw new NotSupportedException(
            $"Model '{Model}' does not support: " +
            string.Join(", ", missing));
    }

    private static void ValidateCapability(
        AiModelCapability capability)
    {
        if (!Enum.IsDefined(capability))
        {
            throw new ArgumentOutOfRangeException(
                nameof(capability),
                capability,
                "Capability value is not recognized.");
        }
    }
}