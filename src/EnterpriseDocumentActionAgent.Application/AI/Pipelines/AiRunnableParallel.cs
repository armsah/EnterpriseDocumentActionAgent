namespace EnterpriseDocumentActionAgent.Application.AI.Pipelines;

/// <summary>
/// Executes multiple independent AI runnables
/// concurrently using a shared input value.
/// </summary>
/// <typeparam name="TInput">
/// Input type accepted by every runnable.
/// </typeparam>
/// <typeparam name="TOutput">
/// Output type produced by every runnable.
/// </typeparam>
/// <remarks>
/// Runnables are started in registration order,
/// but may complete in any order.
///
/// Results are returned in registration order.
///
/// This component does not provide retries,
/// transactions, concurrency limits, or
/// automatic cancellation of sibling operations
/// when one operation fails.
/// </remarks>
public sealed class AiRunnableParallel<TInput, TOutput>
    : IAiRunnable<TInput, IReadOnlyList<TOutput>>
{
    private readonly IReadOnlyList<
        IAiRunnable<TInput, TOutput>> _runnables;

    /// <summary>
    /// Initializes a parallel pipeline.
    /// </summary>
    /// <param name="runnables">
    /// Independent runnables to execute.
    /// </param>
    public AiRunnableParallel(
        IEnumerable<IAiRunnable<TInput, TOutput>> runnables)
    {
        ArgumentNullException.ThrowIfNull(runnables);

        var snapshot = runnables.ToArray();

        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "At least one runnable is required.",
                nameof(runnables));
        }

        if (snapshot.Any(static runnable => runnable is null))
        {
            throw new ArgumentException(
                "Runnable collection cannot contain null entries.",
                nameof(runnables));
        }

        _runnables = Array.AsReadOnly(snapshot);
    }

    /// <summary>
    /// Gets the number of registered runnables.
    /// </summary>
    public int Count => _runnables.Count;

    /// <summary>
    /// Executes every runnable with the same input.
    /// </summary>
    /// <remarks>
    /// The method starts each runnable before awaiting
    /// their collective completion.
    ///
    /// If a runnable throws synchronously while being
    /// started, already-started operations may continue
    /// executing after this method returns a failure.
    ///
    /// All successfully started tasks are awaited when
    /// no synchronous startup failure occurs.
    /// </remarks>
    public async Task<IReadOnlyList<TOutput>> RunAsync(
        TInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tasks = new Task<TOutput>[_runnables.Count];

        for (var index = 0; index < _runnables.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var task = _runnables[index].RunAsync(
                input,
                cancellationToken);

            tasks[index] = task
                ?? throw new InvalidOperationException(
                    $"Runnable at index {index} returned a null task.");
        }

        var results = await Task.WhenAll(tasks)
            .ConfigureAwait(false);

        return Array.AsReadOnly(results);
    }

    /// <summary>
    /// Returns a read-only snapshot of the
    /// registered runnable components.
    /// </summary>
    public IReadOnlyList<IAiRunnable<TInput, TOutput>>
        GetRunnables()
    {
        return _runnables;
    }
}

/// <summary>
/// Provides factory methods for parallel
/// AI runnable execution.
/// </summary>
public static class AiRunnableParallel
{
    /// <summary>
    /// Creates a parallel pipeline from an
    /// enumerable of compatible runnables.
    /// </summary>
    public static AiRunnableParallel<TInput, TOutput>
        Create<TInput, TOutput>(
            IEnumerable<IAiRunnable<TInput, TOutput>> runnables)
    {
        return new AiRunnableParallel<TInput, TOutput>(
            runnables);
    }

    /// <summary>
    /// Creates a parallel pipeline from two
    /// compatible runnables.
    /// </summary>
    public static AiRunnableParallel<TInput, TOutput>
        Create<TInput, TOutput>(
            IAiRunnable<TInput, TOutput> first,
            IAiRunnable<TInput, TOutput> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        return new AiRunnableParallel<TInput, TOutput>(
            new[] { first, second });
    }

    /// <summary>
    /// Creates a parallel pipeline from
    /// asynchronous delegates.
    /// </summary>
    public static AiRunnableParallel<TInput, TOutput>
        FromAsync<TInput, TOutput>(
            IEnumerable<
                Func<
                    TInput,
                    CancellationToken,
                    Task<TOutput>>> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var runnables = handlers
            .Select(handler =>
            {
                ArgumentNullException.ThrowIfNull(handler);

                return AiRunnable.Create(handler);
            })
            .ToArray();

        return new AiRunnableParallel<TInput, TOutput>(
            runnables);
    }

    /// <summary>
    /// Creates a parallel pipeline from
    /// synchronous transformation delegates.
    /// </summary>
    /// <remarks>
    /// Synchronous delegates execute on the
    /// calling thread during pipeline startup.
    ///
    /// This factory does not schedule synchronous
    /// work on the thread pool.
    /// </remarks>
    public static AiRunnableParallel<TInput, TOutput>
        FromSync<TInput, TOutput>(
            IEnumerable<Func<TInput, TOutput>> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var runnables = handlers
            .Select(handler =>
            {
                ArgumentNullException.ThrowIfNull(handler);

                return AiRunnable.FromSync(handler);
            })
            .ToArray();

        return new AiRunnableParallel<TInput, TOutput>(
            runnables);
    }
}