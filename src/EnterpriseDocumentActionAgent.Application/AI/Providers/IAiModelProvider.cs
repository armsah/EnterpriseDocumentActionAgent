namespace EnterpriseDocumentActionAgent.Application.AI.Providers;

/// <summary>
/// Defines a provider-independent interface for
/// invoking artificial intelligence models.
/// </summary>
/// <remarks>
/// Implementations may communicate with cloud-hosted
/// models, locally hosted models, or test doubles.
///
/// The agent runtime must depend on this interface
/// rather than provider-specific SDK implementations.
/// </remarks>
public interface IAiModelProvider
{
    /// <summary>
    /// Gets the unique provider identifier.
    /// </summary>
    /// <example>
    /// openai, azure-openai, ollama
    /// </example>
    string ProviderId { get; }

    /// <summary>
    /// Determines whether this provider supports
    /// the specified model identifier.
    /// </summary>
    /// <param name="model">
    /// The model or deployment identifier.
    /// </param>
    /// <returns>
    /// True when the provider is configured to
    /// handle the specified model.
    /// </returns>
    bool SupportsModel(string model);

    /// <summary>
    /// Executes an AI model request asynchronously.
    /// </summary>
    /// <param name="request">
    /// The validated provider-neutral request.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// A normalized response from the selected model.
    /// </returns>
    Task<AiModelResponse> GenerateAsync(
        AiModelRequest request,
        CancellationToken cancellationToken = default);
}