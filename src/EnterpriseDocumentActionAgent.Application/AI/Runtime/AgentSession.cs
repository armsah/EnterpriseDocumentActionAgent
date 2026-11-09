using EnterpriseDocumentActionAgent.Application.AI.Messages;

namespace EnterpriseDocumentActionAgent.Application.AI.Runtime;

/// <summary>
/// Represents the lifecycle of an agent session.
/// </summary>
public enum AgentSessionStatus
{
    Active = 1,
    Completed = 2
}

/// <summary>
/// Maintains an agent's in-memory conversation state.
/// </summary>
/// <remarks>
/// A session owns one conversation and stores its
/// messages in insertion order.
///
/// Session history is not automatically persisted.
/// </remarks>
public sealed class AgentSession
{
    private readonly object _sync = new();

    private readonly List<AiMessage> _messages = new();

    private AgentSessionStatus _status =
        AgentSessionStatus.Active;

    private DateTimeOffset? _completedAtUtc;

    public Guid SessionId { get; }

    public Guid ConversationId { get; }

    public AgentConfiguration Configuration { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public AgentSessionStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public DateTimeOffset? CompletedAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _completedAtUtc;
            }
        }
    }

    public AgentSession(
        AgentConfiguration configuration,
        Guid? conversationId = null,
        IEnumerable<AiMessage>? initialMessages = null)
    {
        Configuration = configuration
            ?? throw new ArgumentNullException(
                nameof(configuration));

        var id = conversationId ?? Guid.NewGuid();

        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        SessionId = Guid.NewGuid();

        ConversationId = id;

        CreatedAtUtc = DateTimeOffset.UtcNow;

        if (initialMessages is not null)
        {
            var messages = initialMessages.ToArray();

            foreach (var message in messages)
            {
                ValidateMessage(message);
            }

            _messages.AddRange(messages);
        }
    }

    /// <summary>
    /// Adds a message to an active session.
    /// </summary>
    public void AddMessage(AiMessage message)
    {
        ValidateMessage(message);

        lock (_sync)
        {
            EnsureActive();

            _messages.Add(message);
        }
    }

    /// <summary>
    /// Adds multiple messages as one synchronized operation.
    /// </summary>
    public void AddMessages(IEnumerable<AiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var batch = messages.ToArray();

        foreach (var message in batch)
        {
            ValidateMessage(message);
        }

        lock (_sync)
        {
            EnsureActive();

            _messages.AddRange(batch);
        }
    }

    /// <summary>
    /// Returns a snapshot of the conversation history.
    /// </summary>
    public IReadOnlyList<AiMessage> GetMessages()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_messages.ToArray());
        }
    }

    /// <summary>
    /// Returns the current number of messages.
    /// </summary>
    public int MessageCount
    {
        get
        {
            lock (_sync)
            {
                return _messages.Count;
            }
        }
    }

    /// <summary>
    /// Completes the session and prevents further changes.
    /// </summary>
    public void Complete()
    {
        lock (_sync)
        {
            EnsureActive();

            _status = AgentSessionStatus.Completed;

            _completedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Validates message ownership.
    /// </summary>
    private void ValidateMessage(AiMessage? message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.ConversationId != ConversationId)
        {
            throw new ArgumentException(
                "Message belongs to a different conversation.",
                nameof(message));
        }
    }

    /// <summary>
    /// Ensures the session is still active.
    /// </summary>
    private void EnsureActive()
    {
        if (_status != AgentSessionStatus.Active)
        {
            throw new InvalidOperationException(
                "The agent session has already been completed.");
        }
    }
}