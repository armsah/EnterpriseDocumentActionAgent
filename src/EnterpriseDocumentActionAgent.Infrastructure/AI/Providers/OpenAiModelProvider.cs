using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EnterpriseDocumentActionAgent.Application.AI.Messages;
using EnterpriseDocumentActionAgent.Application.AI.Providers;

namespace EnterpriseDocumentActionAgent.Infrastructure.AI.Providers;

/// <summary>
/// Implements text-only AI model generation
/// through the OpenAI Chat Completions API.
/// </summary>
/// <remarks>
/// This provider intentionally supports
/// text-only conversations.
///
/// It does not implement streaming,
/// tool calling, multimodal content,
/// or structured-output enforcement.
///
/// HttpClient lifetime should be managed
/// by the dependency injection container.
/// </remarks>
public sealed class OpenAiModelProvider : IAiModelProvider
{
    public const string DefaultProviderId = "openai";

    public const string DefaultEndpoint =
        "https://api.openai.com/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly HashSet<string> _supportedModels;

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <summary>
    /// Gets the configured endpoint.
    /// </summary>
    public Uri Endpoint => _endpoint;

    /// <summary>
    /// Initializes an OpenAI provider.
    /// </summary>
    public OpenAiModelProvider(
        HttpClient httpClient,
        string apiKey,
        IEnumerable<string> supportedModels,
        string providerId = DefaultProviderId,
        string endpoint = DefaultEndpoint)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        ArgumentNullException.ThrowIfNull(supportedModels);

        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        if (!Uri.TryCreate(
                endpoint,
                UriKind.Absolute,
                out var endpointUri) ||
            endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "The endpoint must be an absolute HTTPS URL.",
                nameof(endpoint));
        }

        var models = supportedModels
            .Where(model =>
                !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (models.Count == 0)
        {
            throw new ArgumentException(
                "At least one supported model is required.",
                nameof(supportedModels));
        }

        _httpClient = httpClient;
        _apiKey = apiKey.Trim();
        _endpoint = endpointUri;
        _supportedModels = models;

        ProviderId = providerId.Trim();
    }

    /// <inheritdoc />
    public bool SupportsModel(string model)
    {
        return !string.IsNullOrWhiteSpace(model) &&
               _supportedModels.Contains(model.Trim());
    }

    /// <summary>
    /// Gets the configured supported models.
    /// </summary>
    public IReadOnlyList<string> GetSupportedModels()
    {
        return Array.AsReadOnly(
            _supportedModels
                .OrderBy(model => model, StringComparer.Ordinal)
                .ToArray());
    }

    /// <inheritdoc />
    public async Task<AiModelResponse> GenerateAsync(
        AiModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (!SupportsModel(request.Model))
        {
            throw new InvalidOperationException(
                $"Model '{request.Model}' is not supported " +
                $"by provider '{ProviderId}'.");
        }

        if (request.Messages.Any(message =>
                message is null ||
                message.ConversationId != request.ConversationId))
        {
            throw new ArgumentException(
                "Every message must belong to the request conversation.",
                nameof(request));
        }

        var messages = BuildMessages(request.Messages);

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_completion_tokens"] = request.MaxOutputTokens
        };

        var json = JsonSerializer.Serialize(
            requestBody,
            JsonOptions);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _endpoint);

        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _apiKey);

        httpRequest.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        httpRequest.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        using var httpResponse =
            await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
                .ConfigureAwait(false);

        var responseJson =
            await httpResponse.Content.ReadAsStringAsync(
                cancellationToken)
                .ConfigureAwait(false);

        if (!httpResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"OpenAI request failed with HTTP " +
                $"{(int)httpResponse.StatusCode} " +
                $"({httpResponse.ReasonPhrase}).",
                inner: null,
                statusCode: httpResponse.StatusCode);
        }

        return ParseResponse(
            request,
            responseJson);
    }

    /// <summary>
    /// Converts framework messages to
    /// text-only API message objects.
    /// </summary>
    private static IReadOnlyList<object> BuildMessages(
        IReadOnlyList<AiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var result = new List<object>(
            messages.Count);

        foreach (var message in messages)
        {
            if (message is null)
            {
                throw new ArgumentException(
                    "Conversation contains a null message.",
                    nameof(messages));
            }

            var role = ConvertRole(message.Role);

            if (message.HasToolCorrelation ||
                message.Role == AiMessageRole.Tool)
            {
                throw new NotSupportedException(
                    "Tool calls and tool observations are not supported" +
                    "by this text-only OpenAI provider.");
            }

            result.Add(new
            {
                role,
                content = message.Text
            });
        }

        if (result.Count == 0)
        {
            throw new ArgumentException(
                "At least one conversation message is required.",
                nameof(messages));
        }

        return result;
    }

    /// <summary>
    /// Maps framework message roles to
    /// Chat Completions API roles.
    /// </summary>
    private static string ConvertRole(
        AiMessageRole role)
    {
        return role switch
        {
            AiMessageRole.System => "system",
            AiMessageRole.Developer => "developer",
            AiMessageRole.User => "user",
            AiMessageRole.Assistant => "assistant",
            _ => throw new NotSupportedException(
                $"Unsupported message role '{role}'.")
        };
    }

    /// <summary>
    /// Parses a successful API response.
    /// </summary>
    private AiModelResponse ParseResponse(
        AiModelRequest request,
        string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new InvalidOperationException(
                "OpenAI returned an empty response.");
        }

        using var document =
            JsonDocument.Parse(responseJson);

        var root = document.RootElement;

        if (!root.TryGetProperty(
                "choices",
                out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "OpenAI response contains no choices.");
        }

        var firstChoice = choices[0];

        if (!firstChoice.TryGetProperty(
                "message",
                out var messageElement) ||
            !messageElement.TryGetProperty(
                "content",
                out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "OpenAI response contains no text content.");
        }

        var text = contentElement.GetString() ?? string.Empty;

        var finishReason = AiModelFinishReason.Other;

        if (firstChoice.TryGetProperty(
                "finish_reason",
                out var finishElement) &&
            finishElement.ValueKind == JsonValueKind.String)
        {
            finishReason = ConvertFinishReason(
                finishElement.GetString());
        }

        AiTokenUsage? usage = null;

        if (root.TryGetProperty(
                "usage",
                out var usageElement) &&
            usageElement.ValueKind == JsonValueKind.Object)
        {
            var inputTokens = ReadInt(
                usageElement,
                "prompt_tokens");

            var outputTokens = ReadInt(
                usageElement,
                "completion_tokens");

            usage = new AiTokenUsage(
                inputTokens,
                outputTokens);
        }

        var responseModel = request.Model;

        if (root.TryGetProperty(
                "model",
                out var modelElement) &&
            modelElement.ValueKind == JsonValueKind.String)
        {
            responseModel =
                modelElement.GetString() ?? request.Model;
        }

        return new AiModelResponse(
            conversationId: request.ConversationId,
            provider: ProviderId,
            model: responseModel,
            text: text,
            finishReason: finishReason,
            tokenUsage: usage);
    }

    /// <summary>
    /// Converts an API finish reason.
    /// </summary>
    private static AiModelFinishReason ConvertFinishReason(
        string? finishReason)
    {
        return finishReason switch
        {
            "stop" => AiModelFinishReason.Completed,
            "length" => AiModelFinishReason.LengthLimit,
            "tool_calls" => AiModelFinishReason.ToolCall,
            "content_filter" => AiModelFinishReason.ContentFiltered,
            _ => AiModelFinishReason.Other
        };
    }

    /// <summary>
    /// Reads a nonnegative integer
    /// property from a JSON object.
    /// </summary>
    private static int ReadInt(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result) ||
            result < 0)
        {
            return 0;
        }

        return result;
    }
}