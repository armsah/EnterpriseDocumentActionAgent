namespace EnterpriseDocumentActionAgent.Application.AI.Pipelines;

/// <summary>
/// Composes two strongly typed AI runnables into
/// a sequential execution pipeline.
/// </summary>
/// <typeparam name="TInput">
/// Input type accepted by the first runnable.
/// </typeparam>
/// <typeparam name="TIntermediate">
/// Output type of the first runnable and input
/// type of the second runnable.
/// </typeparam>
/// <typeparam name="TOutput">
/// Final output type produced by the sequence.
/// </typeparam>
/// <remarks>
/// The second runnable executes only after the
/// first runnable completes successfully.
///
/// Exceptions and cancellation propagate to
/// the caller without being suppressed.
///
/// This class does not implement retries,
/// transactions, or automatic compensation
/// for side effects.
/// </remarks>
public sealed class AiRunnableSequence<
    TInput,
    TIntermediate,
    TOutput> : IAiRunnable<TInput, TOutput>
{
    private readonly IAiRunnable<TInput, TIntermediate>
        _first;

    private readonly IAiRunnable<TIntermediate, TOutput>
        _second;

    /// <summary>
    /// Initializes a sequence containing two
    /// compatible runnable components.
    /// </summary>
    public AiRunnableSequence(
        IAiRunnable<TInput, TIntermediate> first,
        IAiRunnable<TIntermediate, TOutput> second)
    {
        _first = first
            ?? throw new ArgumentNullException(
                nameof(first));

        _second = second
            ?? throw new ArgumentNullException(
                nameof(second));
    }

    /// <summary>
    /// Executes both runnables sequentially.
    /// </summary>
    public async Task<TOutput> RunAsync(
        TInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var intermediate = await _first.RunAsync(
            input,
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var output = await _second.RunAsync(
            intermediate,
            cancellationToken).ConfigureAwait(false);

        return output;
    }

    /// <summary>
    /// Returns the first runnable in the sequence.
    /// </summary>
    public IAiRunnable<TInput, TIntermediate> First =>
        _first;

    /// <summary>
    /// Returns the second runnable in the sequence.
    /// </summary>
    public IAiRunnable<TIntermediate, TOutput> Second =>
        _second;
}

/// <summary>
/// Provides factory and composition methods
/// for sequential AI runnable pipelines.
/// </summary>
public static class AiRunnableSequence
{
    /// <summary>
    /// Creates a sequence from two compatible
    /// runnable components.
    /// </summary>
    public static AiRunnableSequence<
        TInput,
        TIntermediate,
        TOutput> Create<
            TInput,
            TIntermediate,
            TOutput>(
        IAiRunnable<TInput, TIntermediate> first,
        IAiRunnable<TIntermediate, TOutput> second)
    {
        return new AiRunnableSequence<
            TInput,
            TIntermediate,
            TOutput>(
            first,
            second);
    }

    /// <summary>
    /// Appends another runnable to an existing
    /// runnable pipeline.
    /// </summary>
    /// <remarks>
    /// The returned sequence preserves the
    /// original runnable as its first stage.
    ///
    /// Nested sequences allow composition of
    /// more than two operations without requiring
    /// runtime type conversions.
    /// </remarks>
    public static AiRunnableSequence<
        TInput,
        TIntermediate,
        TOutput> Then<
            TInput,
            TIntermediate,
            TOutput>(
        this IAiRunnable<TInput, TIntermediate> first,
        IAiRunnable<TIntermediate, TOutput> second)
    {
        return Create(first, second);
    }

    /// <summary>
    /// Creates a sequence from two synchronous
    /// transformation delegates.
    /// </summary>
    public static AiRunnableSequence<
        TInput,
        TIntermediate,
        TOutput> FromSync<
            TInput,
            TIntermediate,
            TOutput>(
        Func<TInput, TIntermediate> first,
        Func<TIntermediate, TOutput> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var firstRunnable =
            AiRunnable.FromSync(first);

        var secondRunnable =
            AiRunnable.FromSync(second);

        return Create(
            firstRunnable,
            secondRunnable);
    }

    /// <summary>
    /// Creates a sequence from two asynchronous
    /// transformation delegates.
    /// </summary>
    public static AiRunnableSequence<
        TInput,
        TIntermediate,
        TOutput> FromAsync<
            TInput,
            TIntermediate,
            TOutput>(
        Func<
            TInput,
            CancellationToken,
            Task<TIntermediate>> first,
        Func<
            TIntermediate,
            CancellationToken,
            Task<TOutput>> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var firstRunnable =
            AiRunnable.Create(first);

        var secondRunnable =
            AiRunnable.Create(second);

        return Create(
            firstRunnable,
            secondRunnable);
    }
}