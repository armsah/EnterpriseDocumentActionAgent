using System.Collections.Concurrent;

using EnterpriseDocumentActionAgent.Application.AI.Providers;

namespace EnterpriseDocumentActionAgent.Infrastructure.AI.Testing;

/// <summary>
/// Represents a recorded fake AI model invocation.
/// </summary>
public sealed record FakeAiModelInvocation
{
    /// <summary>
    /// Gets the invocation sequence number.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the model request.
    /// </summary>
    public AiModelRequest Request { get; }

    /// <summary>
    /// Gets the invocation timestamp.
    /// </summary>
    public DateTimeOffset InvokedAtUtc { get; }

    /// <summary>
    /// Initializes an invocation record.
    /// </summary>
    public FakeAiModelInvocation(
        long sequenceNumber,
        AiModelRequest request,
        DateTimeOffset invokedAtUtc)
    {
        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequenceNumber));
        }

        ArgumentNullException.ThrowIfNull(request);

        SequenceNumber = sequenceNumber;

        Request = request;

        InvokedAtUtc = invokedAtUtc;
    }
}

/// <summary>
/// Provides a configurable fake AI model provider.
/// </summary>
/// <remarks>
/// This provider is intended for testing and
/// local development.
///
/// It does not call an external model service.
///
/// Requests are recorded before simulated latency,
/// failure injection, or response generation.
///
/// Recorded requests may contain sensitive content.
/// Do not expose request history through public APIs.
/// </remarks>
public sealed class FakeAiModelProvider : IAiModelProvider
{
    /// <summary>
    /// Default provider identifier.
    /// </summary>
    public const string DefaultProviderId = "fake";

    /// <summary>
    /// Default model identifier.
    /// </summary>
    public const string DefaultModel = "fake-model";

    /// <summary>
    /// Default response text.
    /// </summary>
    public const string DefaultResponseText =
        "This is a deterministic fake AI response.";

    private readonly ConcurrentDictionary<string, byte>
        _supportedModels =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentQueue<FakeAiModelInvocation>
        _invocations = new();

    private readonly object _configurationLock = new();

    private Func<AiModelRequest, CancellationToken,
        Task<AiModelResponse>>? _responseFactory;

    private string _responseText;

    private TimeSpan _latency;

    private Exception? _configuredFailure;

    private long _invocationCount;

    /// <summary>
    /// Gets the provider identifier.
    /// </summary>
    public string ProviderId { get; }

    /// <summary>
    /// Gets the total number of invocations.
    /// </summary>
    public long InvocationCount =>
        Interlocked.Read(ref _invocationCount);

    /// <summary>
    /// Gets the configured simulated latency.
    /// </summary>
    public TimeSpan Latency
    {
        get
        {
            lock (_configurationLock)
            {
                return _latency;
            }
        }
    }

    /// <summary>
    /// Gets the configured response text.
    /// </summary>
    public string ResponseText
    {
        get
        {
            lock (_configurationLock)
            {
                return _responseText;
            }
        }
    }

    /// <summary>
    /// Initializes a fake model provider.
    /// </summary>
    public FakeAiModelProvider(
        string providerId = DefaultProviderId,
        string model = DefaultModel,
        string responseText = DefaultResponseText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            model);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            responseText);

        ProviderId = providerId.Trim();

        _responseText = responseText;

        _supportedModels.TryAdd(
            model.Trim(),
            0);
    }

    /// <summary>
    /// Determines whether the provider supports
    /// the specified model.
    /// </summary>
    public bool SupportsModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        return _supportedModels.ContainsKey(
            model.Trim());
    }

    /// <summary>
    /// Registers a supported model.
    /// </summary>
    public FakeAiModelProvider AddSupportedModel(
        string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            model);

        _supportedModels.TryAdd(
            model.Trim(),
            0);

        return this;
    }

    /// <summary>
    /// Removes a supported model.
    /// </summary>
    public bool RemoveSupportedModel(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            model);

        return _supportedModels.TryRemove(
            model.Trim(),
            out _);
    }

    /// <summary>
    /// Returns supported model identifiers.
    /// </summary>
    public IReadOnlyList<string> GetSupportedModels()
    {
        return Array.AsReadOnly(
            _supportedModels.Keys
                .OrderBy(
                    model => model,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    /// <summary>
    /// Configures a fixed response.
    /// </summary>
    /// <remarks>
    /// Configuring a fixed response clears any
    /// previously configured response factory.
    /// </remarks>
    public FakeAiModelProvider SetResponse(
        string responseText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            responseText);

        lock (_configurationLock)
        {
            _responseText = responseText;

            _responseFactory = null;
        }

        return this;
    }

    /// <summary>
    /// Configures an asynchronous response factory.
    /// </summary>
    public FakeAiModelProvider SetResponseFactory(
        Func<AiModelRequest, CancellationToken,
            Task<AiModelResponse>> responseFactory)
    {
        ArgumentNullException.ThrowIfNull(
            responseFactory);

        lock (_configurationLock)
        {
            _responseFactory = responseFactory;
        }

        return this;
    }

    /// <summary>
    /// Configures a synchronous response factory.
    /// </summary>
    public FakeAiModelProvider SetResponseFactory(
        Func<AiModelRequest, AiModelResponse>
            responseFactory)
    {
        ArgumentNullException.ThrowIfNull(
            responseFactory);

        return SetResponseFactory(
            (request, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return Task.FromResult(
                    responseFactory(request));
            });
    }

    /// <summary>
    /// Configures simulated provider latency.
    /// </summary>
    public FakeAiModelProvider SetLatency(
        TimeSpan latency)
    {
        if (latency < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latency));
        }

        lock (_configurationLock)
        {
            _latency = latency;
        }

        return this;
    }

    /// <summary>
    /// Configures a simulated provider failure.
    /// </summary>
    /// <remarks>
    /// The configured exception is thrown on
    /// every invocation until cleared.
    /// </remarks>
    public FakeAiModelProvider SetFailure(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(
            exception);

        lock (_configurationLock)
        {
            _configuredFailure = exception;
        }

        return this;
    }

    /// <summary>
    /// Clears the simulated provider failure.
    /// </summary>
    public FakeAiModelProvider ClearFailure()
    {
        lock (_configurationLock)
        {
            _configuredFailure = null;
        }

        return this;
    }

    /// <summary>
    /// Generates a deterministic model response.
    /// </summary>
    public async Task<AiModelResponse> GenerateAsync(
        AiModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (!SupportsModel(request.Model))
        {
            throw new InvalidOperationException(
                $"Fake provider '{ProviderId}' does not " +
                $"support model '{request.Model}'.");
        }

        var sequenceNumber =
            Interlocked.Increment(
                ref _invocationCount);

        _invocations.Enqueue(
            new FakeAiModelInvocation(
                sequenceNumber,
                request,
                DateTimeOffset.UtcNow));

        Func<AiModelRequest, CancellationToken,
            Task<AiModelResponse>>? responseFactory;

        string responseText;

        TimeSpan latency;

        Exception? configuredFailure;

        lock (_configurationLock)
        {
            responseFactory = _responseFactory;

            responseText = _responseText;

            latency = _latency;

            configuredFailure = _configuredFailure;
        }

        if (latency > TimeSpan.Zero)
        {
            await Task.Delay(
                latency,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (configuredFailure is not null)
        {
            throw configuredFailure;
        }

        if (responseFactory is not null)
        {
            var response = await responseFactory(
                request,
                cancellationToken);

            if (response is null)
            {
                throw new InvalidOperationException(
                    "Fake response factory returned null.");
            }

            if (response.ConversationId !=
                request.ConversationId)
            {
                throw new InvalidOperationException(
                    "Fake response conversation identifier " +
                    "does not match the request.");
            }

            return response;
        }

        return new AiModelResponse(
            conversationId: request.ConversationId,
            provider: ProviderId,
            model: request.Model,
            text: responseText,
            finishReason:
                AiModelFinishReason.Completed,
            tokenUsage: new AiTokenUsage(
                inputTokens: 0,
                outputTokens: 0));
    }

    /// <summary>
    /// Returns a snapshot of recorded invocations.
    /// </summary>
    public IReadOnlyList<FakeAiModelInvocation>
        GetInvocations()
    {
        return Array.AsReadOnly(
            _invocations
                .OrderBy(invocation =>
                    invocation.SequenceNumber)
                .ToArray());
    }

    /// <summary>
    /// Gets the latest recorded invocation.
    /// </summary>
    public FakeAiModelInvocation? GetLatestInvocation()
    {
        return _invocations
            .OrderByDescending(invocation =>
                invocation.SequenceNumber)
            .FirstOrDefault();
    }

    /// <summary>
    /// Clears recorded invocation history.
    /// </summary>
    /// <remarks>
    /// The lifetime invocation counter is not reset.
    /// </remarks>
    public void ClearInvocations()
    {
        while (_invocations.TryDequeue(out _))
        {
        }
    }

    /// <summary>
    /// Restores default fake-provider behavior.
    /// </summary>
    /// <remarks>
    /// Supported models and lifetime invocation
    /// count are preserved.
    /// </remarks>
    public FakeAiModelProvider ResetConfiguration()
    {
        lock (_configurationLock)
        {
            _responseText = DefaultResponseText;

            _responseFactory = null;

            _latency = TimeSpan.Zero;

            _configuredFailure = null;
        }

        return this;
    }
}