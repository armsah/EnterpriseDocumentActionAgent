using EnterpriseDocumentActionAgent.Application.AI.Messages;

namespace EnterpriseDocumentActionAgent.Application.AI.Providers;

/// <summary>
/// Describes why an AI model stopped generating output.
/// </summary>
public enum AiModelFinishReason
{
    Completed = 1,
    LengthLimit = 2,
    ToolCall = 3,
    ContentFiltered = 4,
    Other = 5
}

/// <summary>
/// Represents token consumption reported by an AI provider.
/// </summary>
public sealed record AiTokenUsage
{
    public int InputTokens { get; }
    public int OutputTokens { get; }

    public int TotalTokens => checked(InputTokens + OutputTokens);

    public AiTokenUsage(int inputTokens, int outputTokens)
    {
        if (inputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(inputTokens));

        if (outputTokens < 0)
            throw new ArgumentOutOfRangeException(nameof(outputTokens));

        _ = checked(inputTokens + outputTokens);

        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }
}

/// <summary>
/// Represents a provider-independent AI model request.
/// </summary>
public sealed record AiModelRequest
{
    public Guid ConversationId { get; }
    public string Model { get; }
    public IReadOnlyList<AiMessage> Messages { get; }
    public double Temperature { get; }
    public int MaxOutputTokens { get; }
    public string? CorrelationId { get; }

    public AiModelRequest(
        Guid conversationId,
        string model,
        IEnumerable<AiMessage> messages,
        double temperature = 0.7,
        int maxOutputTokens = 2048,
        string? correlationId = null)
    {
        if (conversationId == Guid.Empty)
            throw new ArgumentException(
                "Conversation identifier is required.",
                nameof(conversationId));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException(
                "Model identifier is required.",
                nameof(model));

        if (model.Length > 200)
            throw new ArgumentException(
                "Model identifier is too long.",
                nameof(model));

        ArgumentNullException.ThrowIfNull(messages);

        var messageList = messages.ToArray();

        if (messageList.Length == 0)
            throw new ArgumentException(
                "At least one message is required.",
                nameof(messages));

        if (messageList.Any(message => message is null))
            throw new ArgumentException(
                "Messages cannot contain null entries.",
                nameof(messages));

        if (messageList.Any(
                message => message.ConversationId != conversationId))
            throw new ArgumentException(
                "All messages must belong to the same conversation.",
                nameof(messages));

        if (double.IsNaN(temperature) ||
            double.IsInfinity(temperature) ||
            temperature < 0 ||
            temperature > 2)
            throw new ArgumentOutOfRangeException(
                nameof(temperature),
                "Temperature must be between 0 and 2.");

        if (maxOutputTokens <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxOutputTokens));

        if (correlationId is not null &&
            (string.IsNullOrWhiteSpace(correlationId) ||
             correlationId.Length > 200))
            throw new ArgumentException(
                "Correlation identifier is invalid.",
                nameof(correlationId));

        ConversationId = conversationId;
        Model = model;
        Messages = Array.AsReadOnly(messageList);
        Temperature = temperature;
        MaxOutputTokens = maxOutputTokens;
        CorrelationId = correlationId;
    }
}

/// <summary>
/// Represents a normalized response from an AI model.
/// </summary>
public sealed record AiModelResponse
{
    public Guid ConversationId { get; }
    public string Provider { get; }
    public string Model { get; }
    public string Text { get; }
    public AiModelFinishReason FinishReason { get; }
    public AiTokenUsage? TokenUsage { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public AiModelResponse(
        Guid conversationId,
        string provider,
        string model,
        string text,
        AiModelFinishReason finishReason,
        AiTokenUsage? tokenUsage = null)
    {
        if (conversationId == Guid.Empty)
            throw new ArgumentException(
                "Conversation identifier is required.",
                nameof(conversationId));

        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException(
                "Provider name is required.",
                nameof(provider));

        if (string.IsNullOrWhiteSpace(model))
            throw new ArgumentException(
                "Model identifier is required.",
                nameof(model));

        ArgumentNullException.ThrowIfNull(text);

        if (!Enum.IsDefined(finishReason))
            throw new ArgumentOutOfRangeException(
                nameof(finishReason));

        if (string.IsNullOrWhiteSpace(text) &&
            finishReason == AiModelFinishReason.Completed)
            throw new ArgumentException(
                "Completed responses must contain text.",
                nameof(text));

        ConversationId = conversationId;
        Provider = provider;
        Model = model;
        Text = text;
        FinishReason = finishReason;
        TokenUsage = tokenUsage;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }
}