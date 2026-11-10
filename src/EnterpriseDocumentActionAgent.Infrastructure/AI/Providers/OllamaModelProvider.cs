using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EnterpriseDocumentActionAgent.Application.AI.Messages;
using EnterpriseDocumentActionAgent.Application.AI.Providers;

namespace EnterpriseDocumentActionAgent.Infrastructure.AI.Providers;

/// <summary>
/// Implements text-only model generation
/// through the native Ollama chat API.
/// </summary>
/// <remarks>
/// This provider communicates with /api/chat.
///
/// Streaming is disabled.
///
/// Tool calling, multimodal content,
/// embeddings, and model management
/// are outside this implementation.
///
/// The configured Ollama endpoint must
/// be trusted. Authentication and TLS
/// termination are deployment concerns.
/// </remarks>
public sealed class OllamaModelProvider : IAiModelProvider
{
    /// <summary>
    /// Default framework provider identifier.
    /// </summary>
    public const string DefaultProviderId = "ollama";

    /// <summary>
    /// Default local Ollama endpoint.
    /// </summary>
    public const string DefaultBaseUrl =
        "http://localhost:11434/";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    private readonly Uri _baseUri;

    private readonly HashSet<string> _supportedModels;

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <summary>
    /// Gets the configured Ollama base URI.
    /// </summary>
    public Uri BaseUri => _baseUri;

    /// <summary>
    /// Initializes the Ollama provider.
    /// </summary>
    /// <param name="httpClient">
    /// HTTP client managed by the caller.
    /// </param>
    /// <param name="supportedModels">
    /// Ollama model identifiers accepted
    /// by this provider.
    /// </param>
    /// <param name="baseUrl">
    /// Ollama server base URL.
    /// </param>
    /// <param name="providerId">
    /// Framework provider identifier.
    /// </param>
    public OllamaModelProvider(
        HttpClient httpClient,
        IEnumerable<string> supportedModels,
        string baseUrl = DefaultBaseUrl,
        string providerId = DefaultProviderId)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        ArgumentNullException.ThrowIfNull(supportedModels);

        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp &&
             parsedUri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(parsedUri.Query) ||
            !string.IsNullOrEmpty(parsedUri.Fragment) ||
            !string.IsNullOrEmpty(parsedUri.UserInfo))
        {
            throw new ArgumentException(
                "Ollama base URL must be an absolute HTTP " +
                "or HTTPS URL without credentials, query, " +
                "or fragment.",
                nameof(baseUrl));
        }

        var models = supportedModels
            .Where(model =>
                !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (models.Count == 0)
        {
            throw new ArgumentException(
                "At least one supported Ollama model is required.",
                nameof(supportedModels));
        }

        _httpClient = httpClient;

        _baseUri = parsedUri;

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
    /// Returns configured model identifiers.
    /// </summary>
    public IReadOnlyList<string> GetSupportedModels()
    {
        return Array.AsReadOnly(
            _supportedModels
                .OrderBy(
                    model => model,
                    StringComparer.Ordinal)
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

        ValidateConversation(request);

        var messages = BuildMessages(request.Messages);

        var requestBody = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = false,
            ["options"] = new Dictionary<string, object?>
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = request.MaxOutputTokens
            }
        };

        var json = JsonSerializer.Serialize(
            requestBody,
            JsonOptions);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildChatUri());

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
                $"Ollama request failed with HTTP " +
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
    /// Validates message ownership.
    /// </summary>
    private static void ValidateConversation(
        AiModelRequest request)
    {
        if (request.Messages.Any(message =>
                message is null ||
                message.ConversationId != request.ConversationId))
        {
            throw new ArgumentException(
                "Every message must belong to " +
                "the request conversation.",
                nameof(request));
        }
    }

    /// <summary>
    /// Builds the Ollama chat URI.
    /// </summary>
    private Uri BuildChatUri()
    {
        var builder = new UriBuilder(_baseUri);

        builder.Path = "/api/chat";

        builder.Query = string.Empty;

        builder.Fragment = string.Empty;

        return builder.Uri;
    }

    /// <summary>
    /// Converts framework messages into
    /// native Ollama chat messages.
    /// </summary>
    private static IReadOnlyList<object> BuildMessages(
        IReadOnlyList<AiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var result = new List<object>(messages.Count);

        foreach (var message in messages)
        {
            if (message is null)
            {
                throw new ArgumentException(
                    "Conversation contains a null message.",
                    nameof(messages));
            }

            if (message.HasToolCorrelation ||
                message.Role == AiMessageRole.Tool)
            {
                throw new NotSupportedException(
                    "Tool calls and tool observations " +
                    "are not supported by this provider.");
            }

            var role = ConvertRole(message.Role);

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
    /// Converts framework message roles
    /// into native Ollama roles.
    /// </summary>
    private static string ConvertRole(
        AiMessageRole role)
    {
        return role switch
        {
            AiMessageRole.System => "system",
            AiMessageRole.Developer => "system",
            AiMessageRole.User => "user",
            AiMessageRole.Assistant => "assistant",
            _ => throw new NotSupportedException(
                $"Unsupported message role '{role}'.")
        };
    }

    /// <summary>
    /// Parses a native Ollama response.
    /// </summary>
    private AiModelResponse ParseResponse(
        AiModelRequest request,
        string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new InvalidOperationException(
                "Ollama returned an empty response.");
        }

        using var document =
            JsonDocument.Parse(responseJson);

        var root = document.RootElement;

        if (!root.TryGetProperty(
                "message",
                out var messageElement) ||
            messageElement.ValueKind != JsonValueKind.Object ||
            !messageElement.TryGetProperty(
                "content",
                out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Ollama response contains no text message.");
        }

        var text =
            contentElement.GetString() ?? string.Empty;

        var finishReason = AiModelFinishReason.Other;

        if (root.TryGetProperty(
                "done",
                out var doneElement) &&
            doneElement.ValueKind == JsonValueKind.True)
        {
            finishReason = AiModelFinishReason.Completed;
        }

        if (root.TryGetProperty(
                "done_reason",
                out var reasonElement) &&
            reasonElement.ValueKind == JsonValueKind.String)
        {
            finishReason = ConvertFinishReason(
                reasonElement.GetString(),
                finishReason);
        }

        AiTokenUsage? usage = null;

        if (root.TryGetProperty(
                "prompt_eval_count",
                out var promptTokensElement) &&
            root.TryGetProperty(
                "eval_count",
                out var completionTokensElement))
        {
            var inputTokens = ReadInt(
                promptTokensElement);

            var outputTokens = ReadInt(
                completionTokensElement);

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
    /// Converts Ollama completion reasons.
    /// </summary>
    private static AiModelFinishReason ConvertFinishReason(
        string? reason,
        AiModelFinishReason fallback)
    {
        return reason switch
        {
            "stop" => AiModelFinishReason.Completed,
            "length" => AiModelFinishReason.LengthLimit,
            _ => fallback
        };
    }

    /// <summary>
    /// Reads a nonnegative token count.
    /// </summary>
    private static int ReadInt(
        JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetInt32(out var result) ||
            result < 0)
        {
            return 0;
        }

        return result;
    }
}