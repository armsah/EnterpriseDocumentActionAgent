using EnterpriseDocumentActionAgent.Workers;
using EnterpriseDocumentActionAgent.Workers.Indexing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// ------------------------------------------------------------
// Application identity
// ------------------------------------------------------------

const string applicationName =
    "EnterpriseDocumentActionAgent.Workers";

builder.Logging.AddConsole();

builder.Logging.AddFilter(
    "Microsoft",
    LogLevel.Warning);

builder.Logging.AddFilter(
    "System",
    LogLevel.Warning);

// ------------------------------------------------------------
// Worker configuration
// ------------------------------------------------------------

var configuration = builder.Configuration;

var indexingEnabled = configuration.GetValue<bool>(
    "Workers:DocumentIndexing:Enabled",
    defaultValue: false);

var developmentWorkerEnabled = configuration.GetValue<bool>(
    "Workers:DevelopmentWorker:Enabled",
    defaultValue: false);

var pollIntervalSeconds = configuration.GetValue<int>(
    "Workers:DocumentIndexing:PollIntervalSeconds",
    defaultValue: 5);

var failureDelaySeconds = configuration.GetValue<int>(
    "Workers:DocumentIndexing:FailureDelaySeconds",
    defaultValue: 10);

// ------------------------------------------------------------
// Configuration validation
// ------------------------------------------------------------

if (pollIntervalSeconds <= 0)
{
    throw new InvalidOperationException(
        "Workers:DocumentIndexing:PollIntervalSeconds " +
        "must be greater than zero.");
}

if (failureDelaySeconds <= 0)
{
    throw new InvalidOperationException(
        "Workers:DocumentIndexing:FailureDelaySeconds " +
        "must be greater than zero.");
}

if (pollIntervalSeconds > 3600)
{
    throw new InvalidOperationException(
        "Workers:DocumentIndexing:PollIntervalSeconds " +
        "cannot exceed 3600 seconds.");
}

if (failureDelaySeconds > 3600)
{
    throw new InvalidOperationException(
        "Workers:DocumentIndexing:FailureDelaySeconds " +
        "cannot exceed 3600 seconds.");
}

var indexingOptions = new DocumentIndexWorkerOptions(
    PollInterval: TimeSpan.FromSeconds(
        pollIntervalSeconds),
    FailureDelay: TimeSpan.FromSeconds(
        failureDelaySeconds));

builder.Services.AddSingleton(indexingOptions);

// ------------------------------------------------------------
// Application services
// ------------------------------------------------------------

// Application and infrastructure dependency registration
// will be introduced through the corresponding
// DependencyInjection.cs files.
//
// Until those adapters are available, the indexing worker
// must not be started.
//
// Required future registrations include:
//
// IDocumentIndexEventSource
// IPublishedDocumentIndexProcessor
// IProcessedDocumentIndexEventStore
// IProcessedDocumentIndexEventFactory
// IPublishedDocumentIndexSnapshotReader
// IDocumentContentIndexer
// IDocumentVectorIndex
// ITextEmbeddingProvider

// ------------------------------------------------------------
// Background service registration
// ------------------------------------------------------------

if (indexingEnabled)
{
    throw new InvalidOperationException(
        "Document indexing is enabled, but the required " +
        "infrastructure adapters have not yet been registered. " +
        "Set Workers:DocumentIndexing:Enabled to false until " +
        "the indexing pipeline is fully configured.");
}

// The template worker is retained temporarily for
// development diagnostics.
//
// It is disabled by default to avoid unnecessary
// background activity.

if (developmentWorkerEnabled)
{
    builder.Services.AddHostedService<Worker>();
}

// ------------------------------------------------------------
// Host startup
// ------------------------------------------------------------

using var host = builder.Build();

var logger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger(applicationName);

logger.LogInformation(
    "{ApplicationName} host initialized.",
    applicationName);

logger.LogInformation(
    "Document indexing enabled: {Enabled}.",
    indexingEnabled);

logger.LogInformation(
    "Development worker enabled: {Enabled}.",
    developmentWorkerEnabled);

logger.LogInformation(
    "Document indexing poll interval: {PollInterval}.",
    indexingOptions.PollInterval);

logger.LogInformation(
    "Document indexing failure delay: {FailureDelay}.",
    indexingOptions.FailureDelay);

if (!indexingEnabled && !developmentWorkerEnabled)
{
    logger.LogWarning(
        "No background services are enabled. " +
        "The host will remain running until shutdown.");
}

await host.RunAsync();