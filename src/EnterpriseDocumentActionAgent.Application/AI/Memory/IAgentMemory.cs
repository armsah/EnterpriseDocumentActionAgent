using EnterpriseDocumentActionAgent.Application.AI.Messages;

namespace EnterpriseDocumentActionAgent.Application.AI.Memory;

/// <summary>
/// Defines a provider-independent memory store
/// for AI agent conversation messages.
/// </summary>
/// <remarks>
/// Memory implementations must preserve message order
/// within a conversation and isolate histories by
/// conversation identifier.
///
/// This contract does not provide authorization,
/// encryption, retention, or tenant isolation beyond
/// the supplied conversation identifier.
/// </remarks>
public interface IAgentMemory
{
    /// <summary>
    /// Appends one message to a conversation.
    /// </summary>
    /// <param name="conversationId">
    /// Identifier of the target conversation.
    /// </param>
    /// <param name="message">
    /// Message to append.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// A task representing completion of the append.
    /// </returns>
    /// <remarks>
    /// Implementations must reject empty conversation
    /// identifiers and messages belonging to a
    /// different conversation.
    /// </remarks>
    Task AppendAsync(
        Guid conversationId,
        AiMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends multiple messages to a conversation.
    /// </summary>
    /// <param name="conversationId">
    /// Identifier of the target conversation.
    /// </param>
    /// <param name="messages">
    /// Messages to append in their supplied order.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// A task representing completion of the append.
    /// </returns>
    /// <remarks>
    /// Implementations should validate the complete
    /// batch before changing stored history.
    ///
    /// Atomic persistence across failures depends on
    /// the storage implementation.
    /// </remarks>
    Task AppendManyAsync(
        Guid conversationId,
        IEnumerable<AiMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the stored messages for a conversation.
    /// </summary>
    /// <param name="conversationId">
    /// Identifier of the conversation.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// A read-only snapshot of messages in append order.
    /// </returns>
    /// <remarks>
    /// An unknown conversation must return an empty
    /// collection rather than creating a conversation.
    /// </remarks>
    Task<IReadOnlyList<AiMessage>> GetMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a conversation has
    /// any stored messages.
    /// </summary>
    /// <param name="conversationId">
    /// Identifier of the conversation.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// True when at least one message is stored;
    /// otherwise false.
    /// </returns>
    Task<bool> HasMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all stored messages for a conversation.
    /// </summary>
    /// <param name="conversationId">
    /// Identifier of the conversation to clear.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the operation.
    /// </param>
    /// <returns>
    /// True when stored conversation history
    /// was removed; otherwise false.
    /// </returns>
    /// <remarks>
    /// Clearing an unknown conversation must not
    /// create a new conversation.
    ///
    /// Implementations must define how clearing
    /// interacts with concurrent append operations.
    /// </remarks>
    Task<bool> ClearAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);
}