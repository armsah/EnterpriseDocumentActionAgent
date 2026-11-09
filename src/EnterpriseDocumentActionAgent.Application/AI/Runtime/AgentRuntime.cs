using EnterpriseDocumentActionAgent.Application.AI.Providers;

namespace EnterpriseDocumentActionAgent.Application.AI.Runtime;

/// <summary>
/// Executes provider-neutral AI model requests through
/// the registered AI model providers.
/// </summary>
/// <remarks>
/// This runtime performs one model invocation per request.
///
/// Agent reasoning loops, tool execution, memory,
/// approvals, and workflow orchestration are handled
/// by separate framework components.
/// </remarks>
public sealed class AgentRuntime
{
    private readonly AiProviderRegistry _providerRegistry;

    public AgentRuntime(AiProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry
            ?? throw new ArgumentNullException(
                nameof(providerRegistry));
    }

    /// <summary>
    /// Executes an AI model request using a compatible provider.
    /// </summary>
    public async Task<AiModelResponse> ExecuteAsync(
        AiModelRequest request,
        IEnumerable<AiModelCapability>? requiredCapabilities = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        var capabilities = requiredCapabilities?.ToArray()
            ?? Array.Empty<AiModelCapability>();

        var provider = _providerRegistry.Resolve(
            request.Model,
            capabilities);

        cancellationToken.ThrowIfCancellationRequested();

        var response = await provider.GenerateAsync(
            request,
            cancellationToken).ConfigureAwait(false);

        ValidateResponse(
            request,
            provider,
            response);

        return response;
    }

    /// <summary>
    /// Executes a request through a specifically selected provider.
    /// </summary>
    public async Task<AiModelResponse> ExecuteWithProviderAsync(
        string providerId,
        AiModelRequest request,
        IEnumerable<AiModelCapability>? requiredCapabilities = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException(
                "Provider identifier is required.",
                nameof(providerId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var provider = _providerRegistry.GetProvider(providerId);

        if (!provider.SupportsModel(request.Model))
        {
            throw new NotSupportedException(
                $"Provider '{provider.ProviderId}' does not " +
                $"support model '{request.Model}'.");
        }

        var capabilities = requiredCapabilities?.ToArray()
            ?? Array.Empty<AiModelCapability>();

        if (capabilities.Length > 0)
        {
            var modelCapabilities =
                _providerRegistry.GetCapabilities(request.Model);

            modelCapabilities.Require(capabilities);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var response = await provider.GenerateAsync(
            request,
            cancellationToken).ConfigureAwait(false);

        ValidateResponse(
            request,
            provider,
            response);

        return response;
    }

    /// <summary>
    /// Determines whether a compatible provider exists.
    /// </summary>
    public bool CanExecute(
        string model,
        IEnumerable<AiModelCapability>? requiredCapabilities = null)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        try
        {
            _providerRegistry.Resolve(
                model,
                requiredCapabilities);

            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates the provider response before returning it.
    /// </summary>
    private static void ValidateResponse(
        AiModelRequest request,
        IAiModelProvider provider,
        AiModelResponse? response)
    {
        if (response is null)
        {
            throw new InvalidOperationException(
                $"Provider '{provider.ProviderId}' returned " +
                "a null AI model response.");
        }

        if (response.ConversationId != request.ConversationId)
        {
            throw new InvalidOperationException(
                "AI model response conversation identifier " +
                "does not match the request.");
        }

        if (!string.Equals(
                response.Model,
                request.Model,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AI model response model identifier " +
                "does not match the request.");
        }

        if (!string.Equals(
                response.Provider,
                provider.ProviderId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AI model response provider identifier " +
                "does not match the selected provider.");
        }
    }
}