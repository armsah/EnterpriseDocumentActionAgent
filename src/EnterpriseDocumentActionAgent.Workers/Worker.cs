using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EnterpriseDocumentActionAgent.Workers;

/// <summary>
/// Provides an optional development-only background
/// heartbeat for verifying worker host lifecycle behavior.
/// </summary>
/// <remarks>
/// This service does not process document ingestion events,
/// execute workflows, or perform agent actions.
///
/// It is registered only when
/// Workers:DevelopmentWorker:Enabled is true.
///
/// The heartbeat is diagnostic information and must not
/// be interpreted as evidence that indexing or other
/// application processing pipelines are operational.
/// </remarks>
public sealed class Worker : BackgroundService
{
    private const int DefaultHeartbeatIntervalSeconds = 30;

    private const int MaximumHeartbeatIntervalSeconds = 3600;

    private readonly ILogger<Worker> _logger;

    private readonly TimeSpan _heartbeatInterval;

    public Worker(
        ILogger<Worker> logger,
        IConfiguration configuration)
    {
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        ArgumentNullException.ThrowIfNull(configuration);

        var intervalSeconds = configuration.GetValue<int>(
            "Workers:DevelopmentWorker:HeartbeatIntervalSeconds",
            DefaultHeartbeatIntervalSeconds);

        if (intervalSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Workers:DevelopmentWorker:" +
                "HeartbeatIntervalSeconds must be " +
                "greater than zero.");
        }

        if (intervalSeconds > MaximumHeartbeatIntervalSeconds)
        {
            throw new InvalidOperationException(
                "Workers:DevelopmentWorker:" +
                "HeartbeatIntervalSeconds cannot " +
                "exceed 3600 seconds.");
        }

        _heartbeatInterval = TimeSpan.FromSeconds(
            intervalSeconds);
    }

    /// <summary>
    /// Executes the development heartbeat until
    /// the host requests shutdown.
    /// </summary>
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Development worker started at {StartedAtUtc}. " +
            "Heartbeat interval: {HeartbeatInterval}. " +
            "Document indexing is not performed by " +
            "this service.",
            DateTimeOffset.UtcNow,
            _heartbeatInterval);

        try
        {
            using var timer = new PeriodicTimer(
                _heartbeatInterval);

            while (await timer.WaitForNextTickAsync(
                stoppingToken))
            {
                _logger.LogInformation(
                    "Development worker heartbeat at " +
                    "{HeartbeatAtUtc}.",
                    DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Development worker cancellation received.");
        }
        finally
        {
            _logger.LogInformation(
                "Development worker stopped at {StoppedAtUtc}.",
                DateTimeOffset.UtcNow);
        }
    }
}