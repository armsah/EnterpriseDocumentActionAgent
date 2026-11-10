using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EnterpriseDocumentActionAgent.Application.AI.Runtime;

/// <summary>
/// Describes the outcome of an instrumented
/// agent execution operation.
/// </summary>
public enum AgentTelemetryOutcome
{
    /// <summary>
    /// Operation completed successfully.
    /// </summary>
    Success = 1,

    /// <summary>
    /// Operation failed.
    /// </summary>
    Failure = 2,

    /// <summary>
    /// Operation was canceled.
    /// </summary>
    Canceled = 3
}

/// <summary>
/// Provides execution tracing and metrics
/// for AI agent operations.
/// </summary>
/// <remarks>
/// Uses built-in .NET diagnostics APIs.
///
/// ActivitySource produces distributed tracing
/// activities when a listener is configured.
///
/// Meter exposes metrics to compatible
/// metric listeners and OpenTelemetry SDKs.
///
/// This class does not configure exporters,
/// telemetry storage, sampling, or retention.
///
/// Avoid placing prompts, model responses,
/// credentials, document contents, or other
/// sensitive data in telemetry attributes.
/// </remarks>
public sealed class AgentExecutionTelemetry : IDisposable
{
    /// <summary>
    /// Default instrumentation name.
    /// </summary>
    public const string DefaultInstrumentationName =
        "EnterpriseDocumentActionAgent.AI";

    /// <summary>
    /// Default instrumentation version.
    /// </summary>
    public const string DefaultInstrumentationVersion =
        "1.0.0";

    private readonly ActivitySource _activitySource;

    private readonly Meter _meter;

    private readonly Counter<long> _executionCounter;

    private readonly Counter<long> _failureCounter;

    private readonly Counter<long> _retryCounter;

    private readonly Counter<long> _modelInvocationCounter;

    private readonly Counter<long> _inputTokenCounter;

    private readonly Counter<long> _outputTokenCounter;

    private readonly Histogram<double> _executionDuration;

    private readonly Histogram<double> _modelDuration;

    private bool _disposed;

    /// <summary>
    /// Gets the instrumentation name.
    /// </summary>
    public string InstrumentationName { get; }

    /// <summary>
    /// Gets the instrumentation version.
    /// </summary>
    public string InstrumentationVersion { get; }

    /// <summary>
    /// Initializes agent execution telemetry.
    /// </summary>
    public AgentExecutionTelemetry(
        string instrumentationName =
            DefaultInstrumentationName,
        string instrumentationVersion =
            DefaultInstrumentationVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            instrumentationName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            instrumentationVersion);

        InstrumentationName =
            instrumentationName.Trim();

        InstrumentationVersion =
            instrumentationVersion.Trim();

        _activitySource = new ActivitySource(
            InstrumentationName,
            InstrumentationVersion);

        _meter = new Meter(
            InstrumentationName,
            InstrumentationVersion);

        _executionCounter =
            _meter.CreateCounter<long>(
                "agent.execution.count",
                unit: "{execution}",
                description:
                    "Total agent execution operations.");

        _failureCounter =
            _meter.CreateCounter<long>(
                "agent.execution.failure.count",
                unit: "{failure}",
                description:
                    "Total failed agent executions.");

        _retryCounter =
            _meter.CreateCounter<long>(
                "agent.execution.retry.count",
                unit: "{retry}",
                description:
                    "Total agent execution retries.");

        _modelInvocationCounter =
            _meter.CreateCounter<long>(
                "agent.model.invocation.count",
                unit: "{invocation}",
                description:
                    "Total AI model invocations.");

        _inputTokenCounter =
            _meter.CreateCounter<long>(
                "agent.model.input_tokens",
                unit: "{token}",
                description:
                    "Total reported model input tokens.");

        _outputTokenCounter =
            _meter.CreateCounter<long>(
                "agent.model.output_tokens",
                unit: "{token}",
                description:
                    "Total reported model output tokens.");

        _executionDuration =
            _meter.CreateHistogram<double>(
                "agent.execution.duration",
                unit: "s",
                description:
                    "Agent execution duration in seconds.");

        _modelDuration =
            _meter.CreateHistogram<double>(
                "agent.model.duration",
                unit: "s",
                description:
                    "AI model invocation duration in seconds.");
    }

    /// <summary>
    /// Starts an activity describing
    /// an agent execution.
    /// </summary>
    public Activity? StartExecution(
        string agentId,
        string? model = null,
        string? providerId = null)
    {
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrWhiteSpace(
            agentId);

        var activity = _activitySource.StartActivity(
            "agent.execute",
            ActivityKind.Internal);

        activity?.SetTag(
            "agent.id",
            agentId.Trim());

        if (!string.IsNullOrWhiteSpace(model))
        {
            activity?.SetTag(
                "gen_ai.request.model",
                model.Trim());
        }

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            activity?.SetTag(
                "gen_ai.provider.name",
                providerId.Trim());
        }

        return activity;
    }

    /// <summary>
    /// Starts an activity describing
    /// an AI model invocation.
    /// </summary>
    public Activity? StartModelInvocation(
        string providerId,
        string model)
    {
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            model);

        var activity = _activitySource.StartActivity(
            "agent.model.generate",
            ActivityKind.Client);

        activity?.SetTag(
            "gen_ai.provider.name",
            providerId.Trim());

        activity?.SetTag(
            "gen_ai.request.model",
            model.Trim());

        return activity;
    }

    /// <summary>
    /// Records the outcome and duration
    /// of an agent execution.
    /// </summary>
    public void RecordExecution(
        string agentId,
        AgentTelemetryOutcome outcome,
        TimeSpan duration,
        string? providerId = null)
    {
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrWhiteSpace(
            agentId);

        ValidateOutcome(outcome);

        ValidateDuration(duration);

        var tags = new TagList
        {
            { "agent.id", agentId.Trim() },
            { "agent.outcome", ToOutcomeName(outcome) }
        };

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            tags.Add(
                "gen_ai.provider.name",
                providerId.Trim());
        }

        _executionCounter.Add(
            1,
            tags);

        _executionDuration.Record(
            duration.TotalSeconds,
            tags);

        if (outcome == AgentTelemetryOutcome.Failure)
        {
            _failureCounter.Add(
                1,
                tags);
        }
    }

    /// <summary>
    /// Records an agent retry.
    /// </summary>
    public void RecordRetry(
        string agentId,
        string operationName,
        int retryAttempt)
    {
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrWhiteSpace(
            agentId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            operationName);

        if (retryAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryAttempt));
        }

        var tags = new TagList
        {
            { "agent.id", agentId.Trim() },
            { "agent.operation", operationName.Trim() }
        };

        _retryCounter.Add(
            1,
            tags);
    }

    /// <summary>
    /// Records an AI model invocation
    /// and its reported token usage.
    /// </summary>
    public void RecordModelInvocation(
        string providerId,
        string model,
        TimeSpan duration,
        int inputTokens = 0,
        int outputTokens = 0)
    {
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            model);

        ValidateDuration(duration);

        if (inputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputTokens));
        }

        if (outputTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputTokens));
        }

        var tags = new TagList
        {
            {
                "gen_ai.provider.name",
                providerId.Trim()
            },
            {
                "gen_ai.request.model",
                model.Trim()
            }
        };

        _modelInvocationCounter.Add(
            1,
            tags);

        _modelDuration.Record(
            duration.TotalSeconds,
            tags);

        if (inputTokens > 0)
        {
            _inputTokenCounter.Add(
                inputTokens,
                tags);
        }

        if (outputTokens > 0)
        {
            _outputTokenCounter.Add(
                outputTokens,
                tags);
        }
    }

    /// <summary>
    /// Marks an activity as successful.
    /// </summary>
    public void RecordSuccess(
        Activity? activity)
    {
        ThrowIfDisposed();

        activity?.SetStatus(
            ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Marks an activity as failed and
    /// records exception metadata.
    /// </summary>
    /// <remarks>
    /// Exception messages and stack traces
    /// are intentionally not recorded because
    /// they may contain sensitive information.
    /// </remarks>
    public void RecordException(
        Activity? activity,
        Exception exception)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(exception);

        if (activity is null)
        {
            return;
        }

        activity.SetStatus(
            ActivityStatusCode.Error);

        activity.SetTag(
            "error.type",
            exception.GetType().FullName);

        var tags = new ActivityTagsCollection
        {
            {
                "exception.type",
                exception.GetType().FullName
            }
        };

        activity.AddEvent(
            new ActivityEvent(
                "exception",
                tags: tags));
    }

    /// <summary>
    /// Marks an activity as canceled.
    /// </summary>
    public void RecordCancellation(
        Activity? activity)
    {
        ThrowIfDisposed();

        activity?.SetTag(
            "agent.outcome",
            "canceled");
    }

    /// <summary>
    /// Executes an operation while
    /// recording traces and metrics.
    /// </summary>
    public async Task<T> TrackExecutionAsync<T>(
        string agentId,
        Func<CancellationToken, Task<T>> operation,
        string? model = null,
        string? providerId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrWhiteSpace(
            agentId);

        ArgumentNullException.ThrowIfNull(operation);

        cancellationToken.ThrowIfCancellationRequested();

        using var activity = StartExecution(
            agentId,
            model,
            providerId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await operation(
                cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            RecordSuccess(activity);

            RecordExecution(
                agentId,
                AgentTelemetryOutcome.Success,
                stopwatch.Elapsed,
                providerId);

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();

            RecordCancellation(activity);

            RecordExecution(
                agentId,
                AgentTelemetryOutcome.Canceled,
                stopwatch.Elapsed,
                providerId);

            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            RecordException(
                activity,
                exception);

            RecordExecution(
                agentId,
                AgentTelemetryOutcome.Failure,
                stopwatch.Elapsed,
                providerId);

            throw;
        }
    }

    /// <summary>
    /// Executes a non-generic operation
    /// while recording traces and metrics.
    /// </summary>
    public async Task TrackExecutionAsync(
        string agentId,
        Func<CancellationToken, Task> operation,
        string? model = null,
        string? providerId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await TrackExecutionAsync(
            agentId,
            async token =>
            {
                await operation(token)
                    .ConfigureAwait(false);

                return true;
            },
            model,
            providerId,
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the instrumentation sources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _activitySource.Dispose();

        _meter.Dispose();
    }

    /// <summary>
    /// Throws when telemetry has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);
    }

    /// <summary>
    /// Validates a telemetry outcome.
    /// </summary>
    private static void ValidateOutcome(
        AgentTelemetryOutcome outcome)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome));
        }
    }

    /// <summary>
    /// Validates a recorded duration.
    /// </summary>
    private static void ValidateDuration(
        TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration));
        }
    }

    /// <summary>
    /// Returns the telemetry representation
    /// of an execution outcome.
    /// </summary>
    private static string ToOutcomeName(
        AgentTelemetryOutcome outcome)
    {
        return outcome switch
        {
            AgentTelemetryOutcome.Success =>
                "success",

            AgentTelemetryOutcome.Failure =>
                "failure",

            AgentTelemetryOutcome.Canceled =>
                "canceled",

            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome))
        };
    }
}