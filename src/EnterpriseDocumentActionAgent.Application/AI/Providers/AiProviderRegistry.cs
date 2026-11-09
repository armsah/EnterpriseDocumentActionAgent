using System.Collections.Concurrent;

namespace EnterpriseDocumentActionAgent.Application.AI.Providers;

/// <summary>
/// Maintains registered AI model providers and
/// their declared model capabilities.
/// </summary>
public sealed class AiProviderRegistry
{
    private readonly ConcurrentDictionary<string, IAiModelProvider>
        _providers = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, AiModelCapabilities>
        _capabilities = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a provider using its unique identifier.
    /// </summary>
    public void Register(IAiModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            throw new ArgumentException(
                "Provider identifier is required.",
                nameof(provider));
        }

        if (!_providers.TryAdd(provider.ProviderId, provider))
        {
            throw new InvalidOperationException(
                $"Provider '{provider.ProviderId}' is already registered.");
        }
    }

    /// <summary>
    /// Registers the capabilities of a model.
    /// </summary>
    public void RegisterCapabilities(
        AiModelCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (!_capabilities.TryAdd(capabilities.Model, capabilities))
        {
            throw new InvalidOperationException(
                $"Capabilities for model '{capabilities.Model}' " +
                "are already registered.");
        }
    }

    /// <summary>
    /// Retrieves a provider by its unique identifier.
    /// </summary>
    public IAiModelProvider GetProvider(string providerId)
    {
        ValidateIdentifier(providerId, nameof(providerId));

        if (_providers.TryGetValue(providerId, out var provider))
        {
            return provider;
        }

        throw new KeyNotFoundException(
            $"AI provider '{providerId}' is not registered.");
    }

    /// <summary>
    /// Finds the first registered provider that supports
    /// the requested model and capabilities.
    /// </summary>
    public IAiModelProvider Resolve(
        string model,
        IEnumerable<AiModelCapability>? required = null)
    {
        ValidateIdentifier(model, nameof(model));

        var requiredCapabilities =
            required?.ToArray() ?? Array.Empty<AiModelCapability>();

        foreach (var capability in requiredCapabilities)
        {
            if (!Enum.IsDefined(capability))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(required),
                    capability,
                    "Capability value is not recognized.");
            }
        }

        if (requiredCapabilities.Length > 0)
        {
            if (!_capabilities.TryGetValue(model, out var capabilities))
            {
                throw new NotSupportedException(
                    $"Capabilities for model '{model}' " +
                    "have not been registered.");
            }

            capabilities.Require(requiredCapabilities);
        }

        foreach (var provider in _providers.Values
                     .OrderBy(provider => provider.ProviderId,
                         StringComparer.OrdinalIgnoreCase))
        {
            if (provider.SupportsModel(model))
            {
                return provider;
            }
        }

        throw new KeyNotFoundException(
            $"No registered AI provider supports model '{model}'.");
    }

    /// <summary>
    /// Gets the capabilities registered for a model.
    /// </summary>
    public AiModelCapabilities GetCapabilities(string model)
    {
        ValidateIdentifier(model, nameof(model));

        if (_capabilities.TryGetValue(model, out var capabilities))
        {
            return capabilities;
        }

        throw new KeyNotFoundException(
            $"Capabilities for model '{model}' are not registered.");
    }

    /// <summary>
    /// Returns a snapshot of registered provider identifiers.
    /// </summary>
    public IReadOnlyList<string> GetProviderIds()
    {
        return _providers.Keys
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Determines whether a provider is registered.
    /// </summary>
    public bool ContainsProvider(string providerId)
    {
        ValidateIdentifier(providerId, nameof(providerId));

        return _providers.ContainsKey(providerId);
    }

    private static void ValidateIdentifier(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Identifier cannot be empty.",
                parameterName);
        }

        if (value.Length > 200)
        {
            throw new ArgumentException(
                "Identifier cannot exceed 200 characters.",
                parameterName);
        }
    }
}