using System.Collections.Concurrent;
using EnterpriseDocumentActionAgent.Application.AI.Messages;

namespace EnterpriseDocumentActionAgent.Application.AI.Memory;

/// <summary>
/// Provides thread-safe, in-memory storage for
/// AI agent conversation messages.
/// </summary>
/// <remarks>
/// Messages are grouped by conversation identifier
/// and returned in append order.
///
/// Storage is process-local and is lost when the
/// application terminates.
///
/// This implementation does not enforce user or
/// tenant authorization.
/// </remarks>
public sealed class InMemoryAgentMemory : IAgentMemory
{
    private sealed class ConversationState
    {
        public object SyncRoot { get; } = new();

        public List<AiMessage> Messages { get; } = new();
    }

    private readonly ConcurrentDictionary<Guid, ConversationState>
        _conversations = new();

    /// <inheritdoc />
    public Task AppendAsync(
        Guid conversationId,
        AiMessage message,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);

        ValidateMessage(conversationId, message);

        cancellationToken.ThrowIfCancellationRequested();

        var state = _conversations.GetOrAdd(
            conversationId,
            static _ => new ConversationState());

        lock (state.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            state.Messages.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AppendManyAsync(
        Guid conversationId,
        IEnumerable<AiMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);

        ArgumentNullException.ThrowIfNull(messages);

        cancellationToken.ThrowIfCancellationRequested();

        var batch = messages.ToArray();

        foreach (var message in batch)
        {
            ValidateMessage(conversationId, message);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (batch.Length == 0)
        {
            return Task.CompletedTask;
        }

        var state = _conversations.GetOrAdd(
            conversationId,
            static _ => new ConversationState());

        lock (state.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            state.Messages.AddRange(batch);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AiMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_conversations.TryGetValue(
                conversationId,
                out var state))
        {
            return Task.FromResult<IReadOnlyList<AiMessage>>(
                Array.Empty<AiMessage>());
        }

        lock (state.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<AiMessage> snapshot =
                Array.AsReadOnly(state.Messages.ToArray());

            return Task.FromResult(snapshot);
        }
    }

    /// <inheritdoc />
    public Task<bool> HasMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_conversations.TryGetValue(
                conversationId,
                out var state))
        {
            return Task.FromResult(false);
        }

        lock (state.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(state.Messages.Count > 0);
        }
    }

    /// <inheritdoc />
    public Task<bool> ClearAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ValidateConversationId(conversationId);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_conversations.TryGetValue(
                conversationId,
                out var state))
        {
            return Task.FromResult(false);
        }

        lock (state.SyncRoot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_conversations.TryGetValue(
                    conversationId,
                    out var currentState) ||
                !ReferenceEquals(currentState, state))
            {
                return Task.FromResult(false);
            }

            var removed = _conversations.TryRemove(
                new KeyValuePair<Guid, ConversationState>(
                    conversationId,
                    state));

            return Task.FromResult(removed);
        }
    }

    private static void ValidateConversationId(
        Guid conversationId)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }
    }

    private static void ValidateMessage(
        Guid conversationId,
        AiMessage? message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ConversationId != conversationId)
        {
            throw new ArgumentException(
                "Message belongs to a different conversation.",
                nameof(message));
        }
    }
}