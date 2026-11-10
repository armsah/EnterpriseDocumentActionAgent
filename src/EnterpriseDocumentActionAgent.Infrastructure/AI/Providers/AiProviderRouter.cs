using System.Collections.Concurrent;
using System.Net;
using EnterpriseDocumentActionAgent.Application.AI.Providers;

namespace EnterpriseDocumentActionAgent.Infrastructure.AI.Providers;

/// <summary>
/// Describes the provider selected for a model.
/// </summary>
public sealed record AiProviderRoute
{
    /// <summary>
    /// Gets the model identifier.
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// Gets the selected provider identifier.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Indicates whether the route was explicitly configured.
    /// </summary>
    public bool IsExplicit { get; }

    /// <summary>
    /// Initializes a provider route.
    /// </summary>
    public AiProviderRoute(
        string model,
        string providerId,
        bool isExplicit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        Model = model.Trim();
        ProviderId = providerId.Trim();
        IsExplicit = isExplicit;
    }
}

/// <summary>
/// Routes AI model requests to registered providers.
/// </summary>
/// <remarks>
/// Routing decisions are based on configured
/// preferences and provider model support.
///
/// Optional fallback applies only to selected
/// transient HTTP and transport failures.
///
/// This router does not provide circuit breaking,
/// cost optimization, provider health monitoring,
/// or automatic model-name translation.
/// </remarks>
public sealed class AiProviderRouter : IAiModelProvider
{
    public const string DefaultProviderId = "router";

    private readonly ConcurrentDictionary<string, IAiModelProvider>
        _providers = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string>
        _preferredRoutes = new(StringComparer.Ordinal);

    private readonly object _configurationLock = new();

    private string? _defaultProviderId;

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <summary>
    /// Gets or sets whether transient failures
    /// may trigger another provider attempt.
    /// </summary>
    public bool EnableFallback { get; set; }

    /// <summary>
    /// Gets the number of registered providers.
    /// </summary>
    public int ProviderCount => _providers.Count;

    /// <summary>
    /// Initializes a provider router.
    /// </summary>
    public AiProviderRouter(
        string providerId = DefaultProviderId,
        bool enableFallback = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        ProviderId = providerId.Trim();
        EnableFallback = enableFallback;
    }

    /// <summary>
    /// Registers an AI model provider.
    /// </summary>
    public void RegisterProvider(IAiModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            throw new ArgumentException(
                "Provider identifier cannot be empty.",
                nameof(provider));
        }

        var providerId = provider.ProviderId.Trim();

        if (string.Equals(
                providerId,
                ProviderId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A provider cannot use the router's identifier.",
                nameof(provider));
        }

        if (ReferenceEquals(provider, this))
        {
            throw new ArgumentException(
                "A router cannot register itself.",
                nameof(provider));
        }

        if (!_providers.TryAdd(providerId, provider))
        {
            throw new InvalidOperationException(
                $"Provider '{providerId}' is already registered.");
        }
    }

    /// <summary>
    /// Removes a registered provider.
    /// </summary>
    public bool UnregisterProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        providerId = providerId.Trim();

        lock (_configurationLock)
        {
            if (!_providers.TryRemove(providerId, out _))
            {
                return false;
            }

            if (string.Equals(
                    _defaultProviderId,
                    providerId,
                    StringComparison.Ordinal))
            {
                _defaultProviderId = null;
            }

            foreach (var route in _preferredRoutes)
            {
                if (string.Equals(
                        route.Value,
                        providerId,
                        StringComparison.Ordinal))
                {
                    _preferredRoutes.TryRemove(
                        route.Key,
                        out _);
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Sets the default provider.
    /// </summary>
    public void SetDefaultProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        providerId = providerId.Trim();

        lock (_configurationLock)
        {
            if (!_providers.ContainsKey(providerId))
            {
                throw new KeyNotFoundException(
                    $"Provider '{providerId}' is not registered.");
            }

            _defaultProviderId = providerId;
        }
    }

    /// <summary>
    /// Clears the default provider.
    /// </summary>
    public void ClearDefaultProvider()
    {
        lock (_configurationLock)
        {
            _defaultProviderId = null;
        }
    }

    /// <summary>
    /// Configures a preferred provider for a model.
    /// </summary>
    public void SetPreferredProvider(
        string model,
        string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        model = model.Trim();
        providerId = providerId.Trim();

        lock (_configurationLock)
        {
            if (!_providers.TryGetValue(
                    providerId,
                    out var provider))
            {
                throw new KeyNotFoundException(
                    $"Provider '{providerId}' is not registered.");
            }

            if (!provider.SupportsModel(model))
            {
                throw new InvalidOperationException(
                    $"Provider '{providerId}' does not support " +
                    $"model '{model}'.");
            }

            _preferredRoutes[model] = providerId;
        }
    }

    /// <summary>
    /// Removes a model's preferred route.
    /// </summary>
    public bool ClearPreferredProvider(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return _preferredRoutes.TryRemove(
            model.Trim(),
            out _);
    }

    /// <summary>
    /// Returns registered provider identifiers.
    /// </summary>
    public IReadOnlyList<string> GetProviderIds()
    {
        return Array.AsReadOnly(
            _providers.Keys
                .OrderBy(
                    providerId => providerId,
                    StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Returns configured preferred routes.
    /// </summary>
    public IReadOnlyList<AiProviderRoute> GetPreferredRoutes()
    {
        return Array.AsReadOnly(
            _preferredRoutes
                .OrderBy(
                    route => route.Key,
                    StringComparer.Ordinal)
                .Select(route =>
                    new AiProviderRoute(
                        route.Key,
                        route.Value,
                        isExplicit: true))
                .ToArray());
    }

    /// <summary>
    /// Resolves the preferred provider for a model.
    /// </summary>
    public AiProviderRoute ResolveRoute(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        model = model.Trim();

        var providers = GetOrderedCandidates(model);

        if (providers.Count == 0)
        {
            throw new InvalidOperationException(
                $"No registered provider supports model '{model}'.");
        }

        var selected = providers[0];

        var isExplicit =
            _preferredRoutes.TryGetValue(
                model,
                out var preferredProviderId) &&
            string.Equals(
                preferredProviderId,
                selected.ProviderId,
                StringComparison.Ordinal);

        return new AiProviderRoute(
            model,
            selected.ProviderId,
            isExplicit);
    }

    /// <inheritdoc />
    public bool SupportsModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return _providers.Values.Any(provider =>
            provider.SupportsModel(model.Trim()));
    }

    /// <inheritdoc />
    public async Task<AiModelResponse> GenerateAsync(
        AiModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        var candidates = GetOrderedCandidates(
            request.Model);

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                $"No registered provider supports model '{request.Model}'.");
        }

        var failures = new List<Exception>();

        foreach (var provider in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await provider.GenerateAsync(
                    request,
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (EnableFallback &&
                      IsTransientFailure(exception))
            {
                failures.Add(exception);
            }

            if (!EnableFallback)
            {
                break;
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "All eligible AI providers failed.",
                failures);
        }

        throw new InvalidOperationException(
            "AI provider routing did not produce a response.");
    }

    /// <summary>
    /// Gets eligible providers in routing order.
    /// </summary>
    private IReadOnlyList<IAiModelProvider> GetOrderedCandidates(
        string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return Array.Empty<IAiModelProvider>();
        }

        model = model.Trim();

        var candidates = _providers.Values
            .Where(provider => provider.SupportsModel(model))
            .OrderBy(
                provider => provider.ProviderId,
                StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            return candidates;
        }

        var preferredProviderId =
            _preferredRoutes.TryGetValue(
                model,
                out var configuredProvider)
                ? configuredProvider
                : null;

        string? defaultProviderId;

        lock (_configurationLock)
        {
            defaultProviderId = _defaultProviderId;
        }

        var priorityProviderId =
            preferredProviderId ?? defaultProviderId;

        if (priorityProviderId is null)
        {
            return candidates;
        }

        var priorityIndex = candidates.FindIndex(
            provider => string.Equals(
                provider.ProviderId,
                priorityProviderId,
                StringComparison.Ordinal));

        if (priorityIndex > 0)
        {
            var priorityProvider = candidates[priorityIndex];

            candidates.RemoveAt(priorityIndex);

            candidates.Insert(0, priorityProvider);
        }

        return candidates;
    }

    /// <summary>
    /// Identifies failures that may be retried
    /// through another provider.
    /// </summary>
    private static bool IsTransientFailure(Exception exception)
    {
        if (exception is HttpRequestException httpException)
        {
            if (!httpException.StatusCode.HasValue)
            {
                return true;
            }

            var statusCode = httpException.StatusCode.Value;

            return statusCode is
                HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout ||
                (int)statusCode >= 500;
        }

        return exception is TimeoutException;
    }
}