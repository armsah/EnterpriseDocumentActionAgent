using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EnterpriseDocumentActionAgent.Application.AI.Messages;
using EnterpriseDocumentActionAgent.Application.AI.Providers;

namespace EnterpriseDocumentActionAgent.Infrastructure.AI.Providers;

/// <summary>
/// Implements text-only AI generation through
/// Azure OpenAI Chat Completions deployments.
/// </summary>
/// <remarks>
/// This provider uses Azure API-key authentication.
///
/// Model identifiers are mapped to deployment names.
///
/// Streaming, tool calling, multimodal input,
/// managed identity, and automatic retries
/// are not implemented.
/// </remarks>
public sealed class AzureOpenAiModelProvider : IAiModelProvider
{
    public const string DefaultProviderId = "azure-openai";

    public const string DefaultApiVersion = "2024-10-21";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _resourceEndpoint;
    private readonly string _apiVersion;

    private readonly IReadOnlyDictionary<string, string>
        _modelDeployments;

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <summary>
    /// Gets the configured Azure resource endpoint.
    /// </summary>
    public Uri ResourceEndpoint => _resourceEndpoint;

    /// <summary>
    /// Gets the configured API version.
    /// </summary>
    public string ApiVersion => _apiVersion;

    /// <summary>
    /// Initializes the Azure OpenAI provider.
    /// </summary>
    /// <param name="httpClient">
    /// HTTP client managed by the caller.
    /// </param>
    /// <param name="apiKey">
    /// Azure OpenAI API key.
    /// </param>
    /// <param name="resourceEndpoint">
    /// HTTPS resource endpoint, for example:
    /// https://example.openai.azure.com/
    /// </param>
    /// <param name="modelDeployments">
    /// Mapping from framework model names
    /// to Azure deployment identifiers.
    /// </param>
    /// <param name="apiVersion">
    /// Azure OpenAI REST API version.
    /// </param>
    /// <param name="providerId">
    /// Framework provider identifier.
    /// </param>
    public AzureOpenAiModelProvider(
        HttpClient httpClient,
        string apiKey,
        string resourceEndpoint,
        IReadOnlyDictionary<string, string> modelDeployments,
        string apiVersion = DefaultApiVersion,
        string providerId = DefaultProviderId)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceEndpoint);
        ArgumentNullException.ThrowIfNull(modelDeployments);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        if (!Uri.TryCreate(
                resourceEndpoint,
                UriKind.Absolute,
                out var parsedEndpoint) ||
            parsedEndpoint.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(parsedEndpoint.Query) ||
            !string.IsNullOrEmpty(parsedEndpoint.Fragment))
        {
            throw new ArgumentException(
                "Azure resource endpoint must be an absolute " +
                "HTTPS URL without query or fragment.",
                nameof(resourceEndpoint));
        }

        if (modelDeployments.Count == 0)
        {
            throw new ArgumentException(
                "At least one model deployment is required.",
                nameof(modelDeployments));
        }

        var deployments =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        foreach (var mapping in modelDeployments)
        {
            if (string.IsNullOrWhiteSpace(mapping.Key))
            {
                throw new ArgumentException(
                    "Model identifiers cannot be empty.",
                    nameof(modelDeployments));
            }

            if (string.IsNullOrWhiteSpace(mapping.Value))
            {
                throw new ArgumentException(
                    "Deployment identifiers cannot be empty.",
                    nameof(modelDeployments));
            }

            var model = mapping.Key.Trim();
            var deployment = mapping.Value.Trim();

            if (!IsValidDeploymentName(deployment))
            {
                throw new ArgumentException(
                    $"Deployment identifier '{deployment}' " +
                    "contains unsupported characters.",
                    nameof(modelDeployments));
            }

            if (!deployments.TryAdd(model, deployment))
            {
                throw new ArgumentException(
                    $"Duplicate model identifier '{model}'.",
                    nameof(modelDeployments));
            }
        }

        _httpClient = httpClient;
        _apiKey = apiKey.Trim();

        _resourceEndpoint = new Uri(
            parsedEndpoint.GetLeftPart(UriPartial.Authority) + "/");

        _apiVersion = apiVersion.Trim();

        _modelDeployments = deployments;

        ProviderId = providerId.Trim();
    }

    /// <inheritdoc />
    public bool SupportsModel(string model)
    {
        return !string.IsNullOrWhiteSpace(model) &&
               _modelDeployments.ContainsKey(model.Trim());
    }

    /// <summary>
    /// Gets the configured model identifiers.
    /// </summary>
    public IReadOnlyList<string> GetSupportedModels()
    {
        return Array.AsReadOnly(
            _modelDeployments.Keys
                .OrderBy(
                    model => model,
                    StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// Resolves an Azure deployment identifier.
    /// </summary>
    public string GetDeploymentName(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        if (!_modelDeployments.TryGetValue(
                model.Trim(),
                out var deployment))
        {
            throw new KeyNotFoundException(
                $"No Azure deployment is configured " +
                $"for model '{model}'.");
        }

        return deployment;
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
                "Every message must belong to " +
                "the request conversation.",
                nameof(request));
        }

        var messages = BuildMessages(request.Messages);

        var deployment = GetDeploymentName(request.Model);

        var requestUri = BuildRequestUri(deployment);

        var requestBody = new Dictionary<string, object?>
        {
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxOutputTokens
        };

        var json = JsonSerializer.Serialize(
            requestBody,
            JsonOptions);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            requestUri);

        httpRequest.Headers.Add(
            "api-key",
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
                $"Azure OpenAI request failed with HTTP " +
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
    /// Builds the deployment-specific
    /// Chat Completions endpoint.
    /// </summary>
    private Uri BuildRequestUri(string deployment)
    {
        var escapedDeployment =
            Uri.EscapeDataString(deployment);

        var escapedVersion =
            Uri.EscapeDataString(_apiVersion);

        var relativePath =
            $"openai/deployments/{escapedDeployment}/" +
            $"chat/completions?api-version={escapedVersion}";

        return new Uri(
            _resourceEndpoint,
            relativePath);
    }

    /// <summary>
    /// Converts framework messages into
    /// text-only API message objects.
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
    /// Converts framework roles to
    /// Chat Completions roles.
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
    /// Parses a successful Azure OpenAI
    /// Chat Completions response.
    /// </summary>
    private AiModelResponse ParseResponse(
        AiModelRequest request,
        string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new InvalidOperationException(
                "Azure OpenAI returned an empty response.");
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
                "Azure OpenAI response contains no choices.");
        }

        var firstChoice = choices[0];

        if (!firstChoice.TryGetProperty(
                "message",
                out var messageElement) ||
            messageElement.ValueKind != JsonValueKind.Object ||
            !messageElement.TryGetProperty(
                "content",
                out var contentElement) ||
            contentElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                "Azure OpenAI response contains no text content.");
        }

        var text =
            contentElement.GetString() ?? string.Empty;

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
    /// Converts an API finish reason
    /// into the framework enum.
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
    /// Reads a nonnegative JSON integer.
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

    /// <summary>
    /// Validates a deployment identifier
    /// before inserting it into a URL path.
    /// </summary>
    private static bool IsValidDeploymentName(
        string deployment)
    {
        if (deployment.Length is < 1 or > 128)
        {
            return false;
        }

        foreach (var character in deployment)
        {
            if (!(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}