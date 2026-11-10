using System.Collections.Concurrent;

namespace EnterpriseDocumentActionAgent.Application.AI.Teams;

/// <summary>
/// Represents an immutable message envelope
/// exchanged between agents.
/// </summary>
/// <remarks>
/// The envelope metadata is immutable.
///
/// The payload is application-defined and
/// may reference a mutable object.
///
/// This message does not prove sender identity
/// or grant authorization to recipients.
/// </remarks>
public sealed record AgentBusMessage
{
    /// <summary>
    /// Gets the unique message identifier.
    /// </summary>
    public Guid MessageId { get; }

    /// <summary>
    /// Gets the sender agent identifier.
    /// </summary>
    public string SenderAgentId { get; }

    /// <summary>
    /// Gets the intended recipient identifier.
    /// </summary>
    /// <remarks>
    /// Null indicates a broadcast message.
    /// </remarks>
    public string? RecipientAgentId { get; }

    /// <summary>
    /// Gets the message type.
    /// </summary>
    public string MessageType { get; }

    /// <summary>
    /// Gets the message payload.
    /// </summary>
    public object? Payload { get; }

    /// <summary>
    /// Gets the optional correlation identifier.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets whether this message is a broadcast.
    /// </summary>
    public bool IsBroadcast => RecipientAgentId is null;

    /// <summary>
    /// Initializes an agent bus message.
    /// </summary>
    public AgentBusMessage(
        string senderAgentId,
        string? recipientAgentId,
        string messageType,
        object? payload = null,
        string? correlationId = null,
        Guid? messageId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            senderAgentId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            messageType);

        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Message ID cannot be empty.",
                nameof(messageId));
        }

        SenderAgentId = senderAgentId.Trim();

        RecipientAgentId =
            string.IsNullOrWhiteSpace(recipientAgentId)
                ? null
                : recipientAgentId.Trim();

        MessageType = messageType.Trim();

        Payload = payload;

        CorrelationId =
            string.IsNullOrWhiteSpace(correlationId)
                ? null
                : correlationId.Trim();

        MessageId = messageId ?? Guid.NewGuid();

        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Creates a direct message.
    /// </summary>
    public static AgentBusMessage Direct(
        string senderAgentId,
        string recipientAgentId,
        string messageType,
        object? payload = null,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            recipientAgentId);

        return new AgentBusMessage(
            senderAgentId,
            recipientAgentId,
            messageType,
            payload,
            correlationId);
    }

    /// <summary>
    /// Creates a broadcast message.
    /// </summary>
    public static AgentBusMessage Broadcast(
        string senderAgentId,
        string messageType,
        object? payload = null,
        string? correlationId = null)
    {
        return new AgentBusMessage(
            senderAgentId,
            recipientAgentId: null,
            messageType: messageType,
            payload: payload,
            correlationId: correlationId);
    }
}

/// <summary>
/// Represents the outcome of delivering
/// a message to a single handler.
/// </summary>
public sealed record AgentMessageDeliveryResult
{
    /// <summary>
    /// Gets the message identifier.
    /// </summary>
    public Guid MessageId { get; }

    /// <summary>
    /// Gets the recipient agent identifier.
    /// </summary>
    public string RecipientAgentId { get; }

    /// <summary>
    /// Gets whether delivery succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets an optional delivery message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the completion timestamp.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Initializes a delivery result.
    /// </summary>
    public AgentMessageDeliveryResult(
        Guid messageId,
        string recipientAgentId,
        bool succeeded,
        string? message = null)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "Message ID cannot be empty.",
                nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            recipientAgentId);

        MessageId = messageId;

        RecipientAgentId = recipientAgentId.Trim();

        Succeeded = succeeded;

        Message = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();

        CompletedAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Defines a consumer of agent bus messages.
/// </summary>
public interface IAgentMessageHandler
{
    /// <summary>
    /// Gets the agent identifier handled
    /// by this consumer.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Processes an incoming message.
    /// </summary>
    Task HandleAsync(
        AgentBusMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides in-memory messaging between
/// registered agent message handlers.
/// </summary>
/// <remarks>
/// Subscriptions are maintained in memory.
///
/// Direct messages target one agent identifier.
///
/// Broadcast messages target all registered
/// agent identifiers except the sender.
///
/// Multiple handlers may subscribe under
/// the same agent identifier.
///
/// Handler invocations occur sequentially
/// in the order of a subscription snapshot.
///
/// Unexpected handler exceptions are
/// represented as failed delivery results.
///
/// Cancellation propagates to the caller.
///
/// The bus does not persist messages,
/// guarantee exactly-once delivery,
/// or enforce authorization.
/// </remarks>
public sealed class AgentMessageBus
{
    private readonly ConcurrentDictionary<
        string,
        HandlerGroup> _subscriptions =
            new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the number of agent identifiers
    /// with registered subscriptions.
    /// </summary>
    public int SubscriberCount =>
        _subscriptions.Count;

    /// <summary>
    /// Registers a message handler.
    /// </summary>
    public AgentMessageBus Subscribe(
        IAgentMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            handler.AgentId);

        var agentId = handler.AgentId.Trim();

        var group = _subscriptions.GetOrAdd(
            agentId,
            _ => new HandlerGroup());

        group.Add(handler);

        return this;
    }

    /// <summary>
    /// Removes a specific handler subscription.
    /// </summary>
    /// <returns>
    /// True when the handler was removed.
    /// </returns>
    public bool Unsubscribe(
        IAgentMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            handler.AgentId);

        var agentId = handler.AgentId.Trim();

        if (!_subscriptions.TryGetValue(
            agentId,
            out var group))
        {
            return false;
        }

        return group.Remove(handler);
    }

    /// <summary>
    /// Returns registered subscriber identifiers.
    /// </summary>
    public IReadOnlyList<string> GetSubscriberIds()
    {
        return Array.AsReadOnly(
            _subscriptions
                .Where(pair => pair.Value.Count > 0)
                .Select(pair => pair.Key)
                .OrderBy(
                    id => id,
                    StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Publishes a message to its recipients.
    /// </summary>
    public async Task<
        IReadOnlyList<AgentMessageDeliveryResult>>
        PublishAsync(
            AgentBusMessage message,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        cancellationToken.ThrowIfCancellationRequested();

        var targets = ResolveTargets(message);

        var results =
            new List<AgentMessageDeliveryResult>();

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var handler in target.Handlers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await handler.HandleAsync(
                        message,
                        cancellationToken)
                        .ConfigureAwait(false);

                    results.Add(
                        new AgentMessageDeliveryResult(
                            message.MessageId,
                            target.AgentId,
                            succeeded: true));
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    results.Add(
                        new AgentMessageDeliveryResult(
                            message.MessageId,
                            target.AgentId,
                            succeeded: false,
                            message: exception.Message));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        return Array.AsReadOnly(results.ToArray());
    }

    /// <summary>
    /// Resolves a stable snapshot of
    /// message delivery targets.
    /// </summary>
    private IReadOnlyList<DeliveryTarget> ResolveTargets(
        AgentBusMessage message)
    {
        var targets = new List<DeliveryTarget>();

        if (!message.IsBroadcast)
        {
            var recipientId = message.RecipientAgentId!;

            if (_subscriptions.TryGetValue(
                recipientId,
                out var group))
            {
                var handlers = group.GetHandlers();

                if (handlers.Count > 0)
                {
                    targets.Add(
                        new DeliveryTarget(
                            recipientId,
                            handlers));
                }
            }

            return targets;
        }

        foreach (var pair in _subscriptions
            .OrderBy(
                pair => pair.Key,
                StringComparer.Ordinal))
        {
            if (string.Equals(
                pair.Key,
                message.SenderAgentId,
                StringComparison.Ordinal))
            {
                continue;
            }

            var handlers = pair.Value.GetHandlers();

            if (handlers.Count == 0)
            {
                continue;
            }

            targets.Add(
                new DeliveryTarget(
                    pair.Key,
                    handlers));
        }

        return targets;
    }

    /// <summary>
    /// Stores handlers for one agent identifier.
    /// </summary>
    private sealed class HandlerGroup
    {
        private readonly object _sync = new();

        private readonly List<IAgentMessageHandler>
            _handlers = new();

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _handlers.Count;
                }
            }
        }

        public void Add(
            IAgentMessageHandler handler)
        {
            lock (_sync)
            {
                if (_handlers.Any(existing =>
                    ReferenceEquals(existing, handler)))
                {
                    throw new InvalidOperationException(
                        "The handler is already subscribed.");
                }

                _handlers.Add(handler);
            }
        }

        public bool Remove(
            IAgentMessageHandler handler)
        {
            lock (_sync)
            {
                for (var index = 0;
                     index < _handlers.Count;
                     index++)
                {
                    if (ReferenceEquals(
                        _handlers[index],
                        handler))
                    {
                        _handlers.RemoveAt(index);

                        return true;
                    }
                }

                return false;
            }
        }

        public IReadOnlyList<IAgentMessageHandler>
            GetHandlers()
        {
            lock (_sync)
            {
                return Array.AsReadOnly(
                    _handlers.ToArray());
            }
        }
    }

    /// <summary>
    /// Represents a resolved recipient
    /// and its subscribed handlers.
    /// </summary>
    private sealed record DeliveryTarget(
        string AgentId,
        IReadOnlyList<IAgentMessageHandler> Handlers);
}