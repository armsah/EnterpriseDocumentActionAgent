using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EnterpriseDocumentActionAgent.Application;

/// <summary>
/// Registers application-layer services with the
/// dependency-injection container.
/// </summary>
/// <remarks>
/// The Application layer depends on abstractions
/// rather than infrastructure implementations.
///
/// Infrastructure adapters must be registered
/// separately by the hosting composition root.
///
/// Registration of an application service does not
/// guarantee that all its constructor dependencies
/// have been configured.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers application-layer services.
    /// </summary>
    /// <param name="services">
    /// The service collection to configure.
    /// </param>
    /// <returns>
    /// The same service collection for fluent
    /// registration.
    /// </returns>
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Application use cases and adapters are
        // registered by the host after their
        // dependency graphs have been verified.
        //
        // This method deliberately avoids registering
        // incomplete services or substituting
        // development implementations for production
        // infrastructure.

        return services;
    }

    /// <summary>
    /// Verifies that required application dependencies
    /// have been registered.
    /// </summary>
    /// <remarks>
    /// This method validates registration presence.
    /// It does not instantiate services or verify
    /// the complete dependency graph.
    /// </remarks>
    public static IServiceCollection RequireApplicationService<TService>(
        this IServiceCollection services)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);

        var serviceType = typeof(TService);

        var registered = services.Any(
            descriptor =>
                descriptor.ServiceType == serviceType);

        if (!registered)
        {
            throw new InvalidOperationException(
                $"Required application service " +
                $"'{serviceType.FullName}' is not registered.");
        }

        return services;
    }

    /// <summary>
    /// Registers an application service only when
    /// no existing registration is present.
    /// </summary>
    /// <typeparam name="TService">
    /// The application service contract.
    /// </typeparam>
    /// <typeparam name="TImplementation">
    /// The concrete application implementation.
    /// </typeparam>
    public static IServiceCollection AddApplicationScoped<
        TService,
        TImplementation>(
        this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<TService, TImplementation>();

        return services;
    }

    /// <summary>
    /// Registers a singleton application service
    /// without replacing an existing registration.
    /// </summary>
    public static IServiceCollection AddApplicationSingleton<
        TService,
        TImplementation>(
        this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TService, TImplementation>();

        return services;
    }

    /// <summary>
    /// Registers a transient application service
    /// without replacing an existing registration.
    /// </summary>
    public static IServiceCollection AddApplicationTransient<
        TService,
        TImplementation>(
        this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<TService, TImplementation>();

        return services;
    }
}