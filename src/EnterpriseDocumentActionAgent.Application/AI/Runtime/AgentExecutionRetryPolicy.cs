using System.Net;
using System.Security.Cryptography;

namespace EnterpriseDocumentActionAgent.Application.AI.Runtime;

/// <summary>
/// Describes a retry attempt scheduled
/// after an operation failure.
/// </summary>
public sealed record AgentExecutionRetryEvent
{
    /// <summary>
    /// Gets the retry attempt number.
    /// </summary>
    /// <remarks>
    /// The first retry has attempt number 1.
    /// </remarks>
    public int RetryAttempt { get; }

    /// <summary>
    /// Gets the maximum permitted retries.
    /// </summary>
    public int MaxRetryAttempts { get; }

    /// <summary>
    /// Gets the exception that triggered the retry.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// Gets the delay before the next attempt.
    /// </summary>
    public TimeSpan Delay { get; }

    /// <summary>
    /// Gets the time when the retry was scheduled.
    /// </summary>
    public DateTimeOffset ScheduledAtUtc { get; }

    /// <summary>
    /// Initializes a retry notification.
    /// </summary>
    public AgentExecutionRetryEvent(
        int retryAttempt,
        int maxRetryAttempts,
        Exception exception,
        TimeSpan delay)
    {
        if (retryAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryAttempt));
        }

        if (maxRetryAttempts < retryAttempt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetryAttempts));
        }

        ArgumentNullException.ThrowIfNull(exception);

        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delay));
        }

        RetryAttempt = retryAttempt;
        MaxRetryAttempts = maxRetryAttempts;
        Exception = exception;
        Delay = delay;
        ScheduledAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Configures retry behavior for asynchronous
/// agent execution operations.
/// </summary>
/// <remarks>
/// The policy retries only exceptions accepted
/// by its retry predicate.
///
/// Cancellation is never treated as retryable.
///
/// The maximum retry count excludes the
/// initial operation attempt.
///
/// This component does not provide
/// circuit breaking, durable retries,
/// distributed coordination, or
/// exactly-once execution guarantees.
/// </remarks>
public sealed class AgentExecutionRetryPolicy
{
    /// <summary>
    /// Default maximum number of retries.
    /// </summary>
    public const int DefaultMaxRetryAttempts = 3;

    /// <summary>
    /// Default initial backoff delay.
    /// </summary>
    public static readonly TimeSpan DefaultInitialDelay =
        TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Default maximum backoff delay.
    /// </summary>
    public static readonly TimeSpan DefaultMaxDelay =
        TimeSpan.FromSeconds(10);

    private readonly Func<Exception, bool> _shouldRetry;

    private readonly Func<
        AgentExecutionRetryEvent,
        CancellationToken,
        Task>? _onRetry;

    /// <summary>
    /// Gets the maximum number of retries.
    /// </summary>
    public int MaxRetryAttempts { get; }

    /// <summary>
    /// Gets the initial backoff delay.
    /// </summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>
    /// Gets the maximum backoff delay.
    /// </summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>
    /// Gets the exponential backoff multiplier.
    /// </summary>
    public double BackoffMultiplier { get; }

    /// <summary>
    /// Gets whether random jitter is enabled.
    /// </summary>
    public bool EnableJitter { get; }

    /// <summary>
    /// Initializes a retry policy.
    /// </summary>
    /// <param name="maxRetryAttempts">
    /// Maximum retries after the first attempt.
    /// </param>
    /// <param name="initialDelay">
    /// Initial retry delay.
    /// </param>
    /// <param name="maxDelay">
    /// Maximum delay between attempts.
    /// </param>
    /// <param name="backoffMultiplier">
    /// Exponential backoff multiplier.
    /// </param>
    /// <param name="enableJitter">
    /// Whether to randomize retry delays.
    /// </param>
    /// <param name="shouldRetry">
    /// Predicate determining whether an
    /// exception is eligible for retry.
    /// </param>
    /// <param name="onRetry">
    /// Optional asynchronous callback
    /// invoked before each retry delay.
    /// </param>
    public AgentExecutionRetryPolicy(
        int maxRetryAttempts = DefaultMaxRetryAttempts,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double backoffMultiplier = 2.0,
        bool enableJitter = true,
        Func<Exception, bool>? shouldRetry = null,
        Func<
            AgentExecutionRetryEvent,
            CancellationToken,
            Task>? onRetry = null)
    {
        if (maxRetryAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRetryAttempts));
        }

        var resolvedInitialDelay =
            initialDelay ?? DefaultInitialDelay;

        var resolvedMaxDelay =
            maxDelay ?? DefaultMaxDelay;

        if (resolvedInitialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialDelay));
        }

        if (resolvedMaxDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDelay));
        }

        if (resolvedMaxDelay < resolvedInitialDelay)
        {
            throw new ArgumentException(
                "Maximum delay cannot be smaller " +
                "than the initial delay.",
                nameof(maxDelay));
        }

        if (double.IsNaN(backoffMultiplier) ||
            double.IsInfinity(backoffMultiplier) ||
            backoffMultiplier < 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(backoffMultiplier));
        }

        MaxRetryAttempts = maxRetryAttempts;
        InitialDelay = resolvedInitialDelay;
        MaxDelay = resolvedMaxDelay;
        BackoffMultiplier = backoffMultiplier;
        EnableJitter = enableJitter;

        _shouldRetry =
            shouldRetry ?? IsTransientFailure;

        _onRetry = onRetry;
    }

    /// <summary>
    /// Executes an asynchronous operation
    /// with the configured retry policy.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        cancellationToken.ThrowIfCancellationRequested();

        for (var retryAttempt = 0; ; retryAttempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
                when (retryAttempt < MaxRetryAttempts &&
                      _shouldRetry(exception))
            {
                var nextRetryAttempt = retryAttempt + 1;

                var delay = CalculateDelay(
                    nextRetryAttempt);

                var retryEvent =
                    new AgentExecutionRetryEvent(
                        nextRetryAttempt,
                        MaxRetryAttempts,
                        exception,
                        delay);

                if (_onRetry is not null)
                {
                    await _onRetry(
                        retryEvent,
                        cancellationToken)
                        .ConfigureAwait(false);
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(
                        delay,
                        cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Executes a non-generic asynchronous
    /// operation with retries.
    /// </summary>
    public async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteAsync(
            async token =>
            {
                await operation(token)
                    .ConfigureAwait(false);

                return true;
            },
            cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Calculates the delay before a retry.
    /// </summary>
    /// <param name="retryAttempt">
    /// One-based retry attempt number.
    /// </param>
    public TimeSpan CalculateDelay(int retryAttempt)
    {
        if (retryAttempt < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryAttempt));
        }

        if (MaxRetryAttempts == 0 ||
            retryAttempt > MaxRetryAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retryAttempt),
                "Retry attempt exceeds the configured limit.");
        }

        if (InitialDelay == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var exponent = retryAttempt - 1;

        var calculatedMilliseconds =
            InitialDelay.TotalMilliseconds *
            Math.Pow(
                BackoffMultiplier,
                exponent);

        var cappedMilliseconds = Math.Min(
            calculatedMilliseconds,
            MaxDelay.TotalMilliseconds);

        if (double.IsInfinity(cappedMilliseconds))
        {
            cappedMilliseconds =
                MaxDelay.TotalMilliseconds;
        }

        if (EnableJitter &&
            cappedMilliseconds > 0)
        {
            // Full jitter distributes retry traffic
            // across the available delay interval.
            var jitterFraction =
                RandomNumberGenerator.GetInt32(
                    0,
                    1_000_001) / 1_000_000.0;

            cappedMilliseconds *= jitterFraction;
        }

        return TimeSpan.FromMilliseconds(
            cappedMilliseconds);
    }

    /// <summary>
    /// Determines whether a failure is
    /// transient under the default policy.
    /// </summary>
    public static bool IsTransientFailure(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return false;
        }

        if (exception is TimeoutException)
        {
            return true;
        }

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

        return false;
    }

    /// <summary>
    /// Creates a retry policy without delays.
    /// </summary>
    /// <remarks>
    /// Intended primarily for deterministic tests.
    /// </remarks>
    public static AgentExecutionRetryPolicy NoDelay(
        int maxRetryAttempts,
        Func<Exception, bool>? shouldRetry = null)
    {
        return new AgentExecutionRetryPolicy(
            maxRetryAttempts: maxRetryAttempts,
            initialDelay: TimeSpan.Zero,
            maxDelay: TimeSpan.Zero,
            enableJitter: false,
            shouldRetry: shouldRetry);
    }
}