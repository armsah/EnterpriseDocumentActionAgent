using EnterpriseDocumentActionAgent.Application.Embeddings;
using EnterpriseDocumentActionAgent.Application.Indexing;
using EnterpriseDocumentActionAgent.Application.Search;
using EnterpriseDocumentActionAgent.Infrastructure.Embeddings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnterpriseDocumentActionAgent.Infrastructure;

/// <summary>
/// Provides dependency injection registration methods
/// for Enterprise Document Action Agent infrastructure.
/// </summary>
/// <remarks>
/// Infrastructure registrations are intentionally
/// separated into:
///
/// 1. Core infrastructure configuration.
/// 2. Azure OpenAI embedding provider registration.
/// 3. Document indexing service registration.
///
/// Database-specific adapters must be registered
/// separately before activating the indexing worker.
///
/// This class does not register placeholder persistence
/// services or silently substitute in-memory storage.
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection
        AddEnterpriseDocumentActionAgentInfrastructure(
            this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }

    /// <summary>
    /// Registers the Azure OpenAI embedding provider
    /// using an externally managed HttpClient.
    /// </summary>
    /// <param name="services">
    /// The application's service collection.
    /// </param>
    /// <param name="httpClient">
    /// The HTTP client supplied by the application host.
    /// </param>
    /// <param name="options">
    /// Azure OpenAI embedding provider configuration.
    /// </param>
    /// <returns>
    /// The service collection for chaining.
    /// </returns>
    public static IServiceCollection
        AddAzureOpenAiEmbeddings(
            this IServiceCollection services,
            HttpClient httpClient,
            AzureOpenAiEmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        ValidateEmbeddingOptions(options);

        services.TryAddSingleton(options);

        services.TryAddSingleton<ITextEmbeddingProvider>(
            _ => new AzureOpenAiEmbeddingProvider(
                httpClient,
                options));

        return services;
    }


    /// <summary>
    /// Registers application indexing services.
    /// </summary>
    /// <remarks>
    /// This method registers the indexing service
    /// graph but does not start a background worker.
    ///
    /// The host must additionally register:
    ///
    /// - ITextEmbeddingProvider
    /// - IDocumentVectorIndex
    /// - IPublishedDocumentIndexSnapshotReader
    ///
    /// These dependencies are validated when the
    /// indexing pipeline is resolved.
    /// </remarks>
    public static IServiceCollection
        AddDocumentIndexingServices(
            this IServiceCollection services,
            DocumentContentIndexingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var indexingOptions =
            options ?? new DocumentContentIndexingOptions();

        ValidateIndexingOptions(indexingOptions);

        services.TryAddSingleton(indexingOptions);

        services.TryAddTransient<
            IDocumentContentIndexer,
            DocumentContentIndexer>();

        services.TryAddTransient<
            IPublishedDocumentIndexProcessor,
            PublishedDocumentIndexProcessor>();

        services.TryAddSingleton<
            IProcessedDocumentIndexEventFactory,
            ProcessedDocumentIndexEventFactory>();

        return services;
    }

    /// <summary>
    /// Checks whether all required indexing service
    /// registrations are present.
    /// </summary>
    /// <remarks>
    /// This method validates registration presence.
    ///
    /// It does not establish database connectivity
    /// or prove that external services are operational.
    ///
    /// Call this method before building the host
    /// when indexing is enabled.
    /// </remarks>
    public static IServiceCollection
        ValidateDocumentIndexingRegistrations(
            this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        RequireRegistration<ITextEmbeddingProvider>(
            services);

        RequireRegistration<IDocumentVectorIndex>(
            services);

        RequireRegistration<
            IPublishedDocumentIndexSnapshotReader>(
                services);

        RequireRegistration<IDocumentContentIndexer>(
            services);

        RequireRegistration<
            IPublishedDocumentIndexProcessor>(
                services);

        RequireRegistration<
            IProcessedDocumentIndexEventStore>(
                services);

        RequireRegistration<
            IProcessedDocumentIndexEventFactory>(
                services);

        return services;
    }

    /// <summary>
    /// Validates the configuration required by
    /// the Azure OpenAI embedding provider.
    /// </summary>
    private static void ValidateEmbeddingOptions(
        AzureOpenAiEmbeddingOptions options)
    {
        if (options.Endpoint is null ||
            !options.Endpoint.IsAbsoluteUri ||
            options.Endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Azure OpenAI endpoint must be an absolute HTTPS URI.",
                nameof(options));
        }

        ValidateRequiredText(
            options.Deployment,
            nameof(options.Deployment),
            256);

        ValidateRequiredText(
            options.ApiKey,
            nameof(options.ApiKey),
            4096);

        ValidateRequiredText(
            options.Model,
            nameof(options.Model),
            256);

        ValidateRequiredText(
            options.ApiVersion,
            nameof(options.ApiVersion),
            64);

        if (options.Dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Embedding dimensions must be positive.");
        }

        if (options.MaximumBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum embedding batch size must be positive.");
        }

        if (options.MaximumInputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum embedding input length must be positive.");
        }
    }

    /// <summary>
    /// Validates deterministic document chunking
    /// configuration.
    /// </summary>
    private static void ValidateIndexingOptions(
        DocumentContentIndexingOptions options)
    {
        if (options.ChunkSizeCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Chunk size must be positive.");
        }

        if (options.ChunkOverlapCharacters < 0 ||
            options.ChunkOverlapCharacters >=
                options.ChunkSizeCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Chunk overlap must be non-negative " +
                "and smaller than chunk size.");
        }

        if (options.MaximumDocumentCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Maximum document length must be positive.");
        }

        if (options.ChunkSizeCharacters >
            options.MaximumDocumentCharacters)
        {
            throw new ArgumentException(
                "Chunk size cannot exceed the maximum document length.",
                nameof(options));
        }
    }

    /// <summary>
    /// Ensures a required service has been registered.
    /// </summary>
    private static void RequireRegistration<TService>(
        IServiceCollection services)
        where TService : class
    {
        var serviceType = typeof(TService);

        var registered = services.Any(
            descriptor =>
                descriptor.ServiceType == serviceType);

        if (!registered)
        {
            throw new InvalidOperationException(
                $"Required document indexing service " +
                $"'{serviceType.FullName}' is not registered.");
        }
    }

    /// <summary>
    /// Validates required provider configuration text.
    /// </summary>
    private static void ValidateRequiredText(
        string value,
        string parameterName,
        int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);

        if (value.Length > maximumLength ||
            value != value.Trim() ||
            value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Provider configuration value is invalid.",
                parameterName);
        }
    }
}