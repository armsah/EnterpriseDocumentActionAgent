namespace EnterpriseDocumentActionAgent.Application.AI.Messages;

/// <summary>
/// Identifies the role of an AI conversation message.
/// </summary>
public enum AiMessageRole
{
    System = 1,
    Developer = 2,
    User = 3,
    Assistant = 4,
    Tool = 5
}

/// <summary>
/// Identifies the trusted origin classification
/// of a conversation message.
/// </summary>
public enum AiMessageSource
{
    Application = 1,
    User = 2,
    Model = 3,
    Tool = 4
}

/// <summary>
/// Represents an immutable, provider-neutral AI message.
/// </summary>
public sealed record AiMessage
{
    public const int MaximumTextCharacters = 64_000;

    public const int MaximumIdentifierCharacters = 200;

    public Guid Id { get; }

    public Guid ConversationId { get; }

    public AiMessageRole Role { get; }

    public AiMessageSource Source { get; }

    public string Text { get; }

    public string? ToolCallId { get; }

    public string? ToolName { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    private AiMessage(
        Guid id,
        Guid conversationId,
        AiMessageRole role,
        AiMessageSource source,
        string text,
        string? toolCallId,
        string? toolName,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ConversationId = conversationId;
        Role = role;
        Source = source;
        Text = text;
        ToolCallId = toolCallId;
        ToolName = toolName;
        CreatedAtUtc = createdAtUtc;
    }

    public static AiMessage System(
        Guid conversationId,
        string instruction)
    {
        return Create(
            conversationId,
            AiMessageRole.System,
            AiMessageSource.Application,
            instruction);
    }

    public static AiMessage Developer(
        Guid conversationId,
        string instruction)
    {
        return Create(
            conversationId,
            AiMessageRole.Developer,
            AiMessageSource.Application,
            instruction);
    }

    public static AiMessage User(
        Guid conversationId,
        string text)
    {
        return Create(
            conversationId,
            AiMessageRole.User,
            AiMessageSource.User,
            text);
    }

    public static AiMessage Assistant(
        Guid conversationId,
        string text)
    {
        return Create(
            conversationId,
            AiMessageRole.Assistant,
            AiMessageSource.Model,
            text);
    }

    public static AiMessage AssistantToolCall(
        Guid conversationId,
        string text,
        string toolCallId,
        string toolName)
    {
        ValidateIdentifier(
            toolCallId,
            nameof(toolCallId));

        ValidateIdentifier(
            toolName,
            nameof(toolName));

        return Create(
            conversationId,
            AiMessageRole.Assistant,
            AiMessageSource.Model,
            text,
            toolCallId,
            toolName);
    }

    /// <summary>
    /// Creates an untrusted tool observation.
    /// </summary>
    public static AiMessage Tool(
        Guid conversationId,
        string toolCallId,
        string toolName,
        string output)
    {
        ValidateIdentifier(
            toolCallId,
            nameof(toolCallId));

        ValidateIdentifier(
            toolName,
            nameof(toolName));

        return Create(
            conversationId,
            AiMessageRole.Tool,
            AiMessageSource.Tool,
            output,
            toolCallId,
            toolName);
    }

    public bool IsToolObservation =>
        Role == AiMessageRole.Tool;

    public bool IsApplicationInstruction =>
        Source == AiMessageSource.Application &&
        (Role == AiMessageRole.System ||
         Role == AiMessageRole.Developer);

    public bool HasToolCorrelation =>
        !string.IsNullOrWhiteSpace(ToolCallId) &&
        !string.IsNullOrWhiteSpace(ToolName);

    private static AiMessage Create(
        Guid conversationId,
        AiMessageRole role,
        AiMessageSource source,
        string text,
        string? toolCallId = null,
        string? toolName = null)
    {
        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Conversation identifier cannot be empty.",
                nameof(conversationId));
        }

        ArgumentNullException.ThrowIfNull(text);

        if (text.Length > MaximumTextCharacters)
        {
            throw new ArgumentException(
                $"Message text cannot exceed " +
                $"{MaximumTextCharacters} characters.",
                nameof(text));
        }

        if (string.IsNullOrWhiteSpace(text) &&
            role != AiMessageRole.Tool)
        {
            throw new ArgumentException(
                "Message text cannot be empty.",
                nameof(text));
        }

        return new AiMessage(
            Guid.NewGuid(),
            conversationId,
            role,
            source,
            text,
            toolCallId,
            toolName,
            DateTimeOffset.UtcNow);
    }

    private static void ValidateIdentifier(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Identifier cannot be empty.",
                parameterName);
        }

        if (value.Length > MaximumIdentifierCharacters)
        {
            throw new ArgumentException(
                $"Identifier cannot exceed " +
                $"{MaximumIdentifierCharacters} characters.",
                parameterName);
        }
    }
}