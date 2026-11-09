namespace EnterpriseDocumentActionAgent.Application.AI.Pipelines;

/// <summary>
/// Defines a strongly typed, asynchronous operation
/// that can participate in an AI execution pipeline.
/// </summary>
/// <typeparam name="TInput">
/// Type of the value accepted by the runnable.
/// </typeparam>
/// <typeparam name="TOutput">
/// Type of the value produced by the runnable.
/// </typeparam>
/// <remarks>
/// Runnable implementations should represent one
/// logical execution step.
///
/// This interface does not prescribe whether a
/// runnable performs local computation, model
/// inference, retrieval, or external I/O.
///
/// Implementations are responsible for honoring
/// cancellation and documenting their side effects.
/// </remarks>
public interface IAiRunnable<in TInput, TOutput>
{
    /// <summary>
    /// Executes the runnable asynchronously.
    /// </summary>
    /// <param name="input">
    /// Input supplied to the execution step.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to request cancellation.
    /// </param>
    /// <returns>
    /// A task containing the output produced by
    /// the runnable.
    /// </returns>
    Task<TOutput> RunAsync(
        TInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapts an asynchronous delegate to the
/// IAiRunnable execution contract.
/// </summary>
/// <typeparam name="TInput">
/// Input type accepted by the delegate.
/// </typeparam>
/// <typeparam name="TOutput">
/// Output type returned by the delegate.
/// </typeparam>
public sealed class AiDelegateRunnable<TInput, TOutput>
    : IAiRunnable<TInput, TOutput>
{
    private readonly Func<
        TInput,
        CancellationToken,
        Task<TOutput>> _handler;

    public AiDelegateRunnable(
        Func<TInput, CancellationToken, Task<TOutput>> handler)
    {
        _handler = handler
            ?? throw new ArgumentNullException(
                nameof(handler));
    }

    /// <inheritdoc />
    public Task<TOutput> RunAsync(
        TInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var task = _handler(input, cancellationToken);

        return task
            ?? throw new InvalidOperationException(
                "Runnable handler returned a null task.");
    }
}

/// <summary>
/// Provides factory methods for creating
/// delegate-backed AI pipeline runnables.
/// </summary>
public static class AiRunnable
{
    /// <summary>
    /// Creates a runnable from an asynchronous delegate.
    /// </summary>
    public static IAiRunnable<TInput, TOutput> Create<TInput, TOutput>(
        Func<TInput, CancellationToken, Task<TOutput>> handler)
    {
        return new AiDelegateRunnable<TInput, TOutput>(
            handler);
    }

    /// <summary>
    /// Creates a runnable from a synchronous delegate.
    /// </summary>
    /// <remarks>
    /// The synchronous delegate executes on the calling
    /// thread. This method does not schedule work on
    /// the thread pool.
    /// </remarks>
    public static IAiRunnable<TInput, TOutput> FromSync<TInput, TOutput>(
        Func<TInput, TOutput> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return new AiDelegateRunnable<TInput, TOutput>(
            (input, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var output = handler(input);

                return Task.FromResult(output);
            });
    }

    /// <summary>
    /// Creates a runnable that returns its input unchanged.
    /// </summary>
    public static IAiRunnable<T, T> Identity<T>()
    {
        return FromSync<T, T>(
            static input => input);
    }
}