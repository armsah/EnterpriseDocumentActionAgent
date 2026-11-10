using System.Data.Common;

using EnterpriseDocumentActionAgent.Application.AI.Graphs;
using EnterpriseDocumentActionAgent.Application.AI.Memory;
using EnterpriseDocumentActionAgent.Application.AI.Providers;
using EnterpriseDocumentActionAgent.Application.AI.Runtime;
using EnterpriseDocumentActionAgent.Application.AI.Tools;

using EnterpriseDocumentActionAgent.Infrastructure.AI.Graphs;
using EnterpriseDocumentActionAgent.Infrastructure.AI.Providers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnterpriseDocumentActionAgent.Infrastructure.AI.DependencyInjection;

/// <summary>
/// Provides dependency injection registration methods
/// for the universal AI agent framework.
/// </summary>
/// <remarks>
/// Core framework services are registered with
/// appropriate dependency injection lifetimes.
///
/// Model providers must be registered explicitly.
///
/// PostgreSQL checkpoint persistence is optional.
///
/// Registration does not automatically enable
/// tool authorization, database migrations,
/// distributed tracing exporters, or external
/// model-provider credentials.
/// </remarks>
public static class UniversalAgentServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core AI agent framework services.
    /// </summary>
    /// <remarks>
    /// Existing registrations are preserved where possible.
    ///
    /// The provider registry, router, memory store,
    /// telemetry, and tool dispatcher are shared
    /// within the dependency injection container.
    ///
    /// Agent-specific configuration and authorization
    /// policies must be supplied separately.
    /// </remarks>
    public static IServiceCollection AddUniversalAgentFramework(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<AiProviderRegistry>();

        services.TryAddSingleton<AiProviderRouter>();

        services.TryAddSingleton<IAiModelProvider>(
            serviceProvider =>
                serviceProvider.GetRequiredService<AiProviderRouter>());

        services.TryAddSingleton<InMemoryAgentMemory>();

        services.TryAddSingleton<IAgentMemory>(
            serviceProvider =>
                serviceProvider.GetRequiredService<InMemoryAgentMemory>());

        services.TryAddSingleton<AgentCapabilityRegistry>();

        services.TryAddSingleton<AgentToolDispatcher>();

        services.TryAddSingleton<AgentExecutionRetryPolicy>();

        services.TryAddSingleton<AgentExecutionTelemetry>();
        AddFrameworkInitializer(services);

        return services;
    }

    /// <summary>
    /// Registers the core framework and configures
    /// its provider router.
    /// </summary>
    public static IServiceCollection AddUniversalAgentFramework(
        this IServiceCollection services,
        Action<AiProviderRouter> configureRouter)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRouter);

        services.AddUniversalAgentFramework();

        services.AddSingleton<
            UniversalAgentRouterConfigurationMarker>(
            new UniversalAgentRouterConfigurationMarker(
                configureRouter));

        return services;
    }

    /// <summary>
    /// Registers a model provider instance.
    /// </summary>
    /// <remarks>
    /// The same provider instance is added to the
    /// provider registry and provider router.
    ///
    /// The provider is registered during service
    /// resolution, not immediately when this method
    /// is called.
    ///
    /// Duplicate provider identifiers are rejected
    /// by the registry and router.
    /// </remarks>
    public static IServiceCollection AddUniversalAgentProvider(
        this IServiceCollection services,
        IAiModelProvider provider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(provider);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            provider.ProviderId);

        services.AddUniversalAgentFramework();

        services.AddSingleton(
            new UniversalAgentProviderRegistration(
                provider));

        return services;
    }

    /// <summary>
    /// Registers a model provider through a factory.
    /// </summary>
    /// <typeparam name="TProvider">
    /// Concrete model provider type.
    /// </typeparam>
    /// <remarks>
    /// The factory is invoked once for the
    /// registration during provider initialization.
    /// </remarks>
    public static IServiceCollection AddUniversalAgentProvider<TProvider>(
        this IServiceCollection services,
        Func<IServiceProvider, TProvider> providerFactory)
        where TProvider : class, IAiModelProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(providerFactory);

        services.AddUniversalAgentFramework();

        services.AddSingleton(
            new UniversalAgentProviderFactoryRegistration(
                serviceProvider =>
                    providerFactory(serviceProvider)));

        return services;
    }

    /// <summary>
    /// Registers a custom retry policy.
    /// </summary>
    public static IServiceCollection AddUniversalAgentRetryPolicy(
        this IServiceCollection services,
        AgentExecutionRetryPolicy retryPolicy)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(retryPolicy);

        services.AddUniversalAgentFramework();

        services.Replace(
            ServiceDescriptor.Singleton(
                retryPolicy));

        return services;
    }

    /// <summary>
    /// Registers a custom telemetry instance.
    /// </summary>
    /// <remarks>
    /// The dependency injection container owns
    /// the registered instance and will dispose it
    /// when the container is disposed.
    /// </remarks>
    public static IServiceCollection AddUniversalAgentTelemetry(
        this IServiceCollection services,
        string instrumentationName,
        string instrumentationVersion =
            AgentExecutionTelemetry.DefaultInstrumentationVersion)
    {
        ArgumentNullException.ThrowIfNull(services);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            instrumentationName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            instrumentationVersion);

        services.AddUniversalAgentFramework();

        services.Replace(
            ServiceDescriptor.Singleton(
                new AgentExecutionTelemetry(
                    instrumentationName,
                    instrumentationVersion)));

        return services;
    }

    /// <summary>
    /// Registers PostgreSQL-backed graph checkpoints.
    /// </summary>
    /// <param name="services">
    /// Service collection to configure.
    /// </param>
    /// <param name="connectionFactory">
    /// Factory that creates a new database connection
    /// for each checkpoint-store operation.
    /// </param>
    /// <param name="tableName">
    /// Name of the PostgreSQL checkpoint table.
    /// </param>
    /// <remarks>
    /// The connection factory must return a new,
    /// independently disposable connection each time.
    ///
    /// This method does not install a PostgreSQL driver,
    /// create the database schema, or test connectivity.
    ///
    /// Call EnsureSchemaAsync during application
    /// initialization when schema creation is desired.
    /// </remarks>
    public static IServiceCollection AddUniversalAgentPostgresCheckpoints(
        this IServiceCollection services,
        Func<DbConnection> connectionFactory,
        string tableName =
            PostgresAgentCheckpointStore.DefaultTableName)
    {
        ArgumentNullException.ThrowIfNull(services);

        ArgumentNullException.ThrowIfNull(
            connectionFactory);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            tableName);

        services.AddUniversalAgentFramework();

        services.AddSingleton(
            new UniversalAgentCheckpointConfiguration(
                connectionFactory,
                tableName));

        services.Replace(
            ServiceDescriptor.Singleton<
                PostgresAgentCheckpointStore>(
                serviceProvider =>
                {
                    var configuration =
                        serviceProvider.GetRequiredService<
                            UniversalAgentCheckpointConfiguration>();

                    return new PostgresAgentCheckpointStore(
                        configuration.ConnectionFactory,
                        configuration.TableName);
                }));

        services.Replace(
            ServiceDescriptor.Singleton<IAgentCheckpointStore>(
                serviceProvider =>
                    serviceProvider.GetRequiredService<
                        PostgresAgentCheckpointStore>()));

        return services;
    }

    /// <summary>
    /// Initializes registered model providers
    /// and configures the provider router.
    /// </summary>
    /// <remarks>
    /// Call this once after the service provider
    /// has been built and before handling requests.
    ///
    /// Provider registration and router configuration
    /// should not be modified concurrently with
    /// active model invocations.
    ///
    /// This method is idempotent for a given
    /// service provider.
    /// </remarks>
    public static IServiceProvider InitializeUniversalAgentFramework(
        this IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(
            serviceProvider);

        var initializer =
            serviceProvider.GetService<
                UniversalAgentFrameworkInitializer>();

        if (initializer is null)
        {
            throw new InvalidOperationException(
                "Universal agent framework services " +
                "have not been registered.");
        }

        initializer.Initialize();

        return serviceProvider;
    }

    /// <summary>
    /// Registers the framework initializer.
    /// </summary>
    /// <remarks>
    /// This method is used internally to ensure
    /// the initialization service is available.
    /// </remarks>
    private static IServiceCollection AddFrameworkInitializer(
        IServiceCollection services)
    {
        services.TryAddSingleton<
            UniversalAgentFrameworkInitializer>();

        return services;
    }

    /// <summary>
    /// Holds an explicitly registered provider.
    /// </summary>
    private sealed class UniversalAgentProviderRegistration
    {
        public IAiModelProvider Provider { get; }

        public UniversalAgentProviderRegistration(
            IAiModelProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);

            Provider = provider;
        }
    }

    /// <summary>
    /// Holds a provider factory.
    /// </summary>
    private sealed class UniversalAgentProviderFactoryRegistration
    {
        public Func<IServiceProvider, IAiModelProvider>
            Factory
        { get; }

        public UniversalAgentProviderFactoryRegistration(
            Func<IServiceProvider, IAiModelProvider> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            Factory = factory;
        }
    }

    /// <summary>
    /// Holds router configuration.
    /// </summary>
    private sealed class UniversalAgentRouterConfigurationMarker
    {
        public Action<AiProviderRouter> Configure { get; }

        public UniversalAgentRouterConfigurationMarker(
            Action<AiProviderRouter> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);

            Configure = configure;
        }
    }

    /// <summary>
    /// Holds checkpoint-store configuration.
    /// </summary>
    private sealed class UniversalAgentCheckpointConfiguration
    {
        public Func<DbConnection> ConnectionFactory { get; }

        public string TableName { get; }

        public UniversalAgentCheckpointConfiguration(
            Func<DbConnection> connectionFactory,
            string tableName)
        {
            ArgumentNullException.ThrowIfNull(
                connectionFactory);

            ArgumentException.ThrowIfNullOrWhiteSpace(
                tableName);

            ConnectionFactory = connectionFactory;
            TableName = tableName;
        }
    }

    /// <summary>
    /// Initializes provider registrations
    /// exactly once per service provider.
    /// </summary>
    private sealed class UniversalAgentFrameworkInitializer
    {
        private readonly IServiceProvider _serviceProvider;

        private readonly AiProviderRegistry _registry;

        private readonly AiProviderRouter _router;

        private readonly object _sync = new();

        private bool _initialized;

        public UniversalAgentFrameworkInitializer(
            IServiceProvider serviceProvider,
            AiProviderRegistry registry,
            AiProviderRouter router)
        {
            ArgumentNullException.ThrowIfNull(
                serviceProvider);

            ArgumentNullException.ThrowIfNull(registry);

            ArgumentNullException.ThrowIfNull(router);

            _serviceProvider = serviceProvider;
            _registry = registry;
            _router = router;
        }

        public void Initialize()
        {
            lock (_sync)
            {
                if (_initialized)
                {
                    return;
                }

                var registeredProviders =
                    _serviceProvider.GetServices<
                        UniversalAgentProviderRegistration>();

                var factoryRegistrations =
                    _serviceProvider.GetServices<
                        UniversalAgentProviderFactoryRegistration>();

                var providers = new List<IAiModelProvider>();

                foreach (var registration in registeredProviders)
                {
                    providers.Add(registration.Provider);
                }

                foreach (var registration in factoryRegistrations)
                {
                    var provider = registration.Factory(
                        _serviceProvider);

                    if (provider is null)
                    {
                        throw new InvalidOperationException(
                            "An AI provider factory returned null.");
                    }

                    providers.Add(provider);
                }

                var providerIds = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var provider in providers)
                {
                    if (string.IsNullOrWhiteSpace(
                        provider.ProviderId))
                    {
                        throw new InvalidOperationException(
                            "A model provider has an empty identifier.");
                    }

                    if (!providerIds.Add(provider.ProviderId))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate AI provider identifier: " +
                            $"'{provider.ProviderId}'.");
                    }

                    if (string.Equals(
                        provider.ProviderId,
                        _router.ProviderId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "A model provider cannot use the " +
                            "same identifier as the router.");
                    }
                }

                foreach (var provider in providers)
                {
                    _registry.Register(provider);
                    _router.RegisterProvider(provider);
                }

                var routerConfigurations =
                    _serviceProvider.GetServices<
                        UniversalAgentRouterConfigurationMarker>();

                foreach (var configuration in routerConfigurations)
                {
                    configuration.Configure(_router);
                }

                _initialized = true;
            }
        }
    }
}