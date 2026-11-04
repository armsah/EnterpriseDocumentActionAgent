using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnterpriseDocumentActionAgent.Application.Embeddings;

namespace EnterpriseDocumentActionAgent.Infrastructure.Embeddings;

/// <summary>
/// Configuration for Azure OpenAI embedding generation.
/// </summary>
/// <remarks>
/// Endpoint must be the Azure OpenAI resource root,
/// for example https://example.openai.azure.com/.
///
/// Deployment identifies the deployed embedding model.
///
/// Model identifies the embedding model reported to
/// the application and used for vector compatibility.
///
/// ApiKey must be loaded from secure configuration.
/// </remarks>
public sealed record AzureOpenAiEmbeddingOptions(
    Uri Endpoint,
    string Deployment,
    string ApiKey,
    string Model,
    int Dimensions,
    int MaximumBatchSize = 16,
    int MaximumInputCharacters = 12000,
    string ApiVersion = "2024-10-21");

/// <summary>
/// Generates text embeddings through the
/// Azure OpenAI REST API.
/// </summary>
/// <remarks>
/// This provider uses an injected HttpClient.
///
/// The hosting application owns HttpClient lifetime
/// and should register the provider using IHttpClientFactory.
///
/// The provider does not log source text or API keys.
///
/// Authentication, tenant isolation, and source-document
/// authorization must be enforced by calling layers.
/// </remarks>
public sealed class AzureOpenAiEmbeddingProvider
    : ITextEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly AzureOpenAiEmbeddingOptions _options;
    private readonly Uri _requestUri;

    /// <inheritdoc />
    public TextEmbeddingProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Creates an Azure OpenAI embedding provider.
    /// </summary>
    public AzureOpenAiEmbeddingProvider(
        HttpClient httpClient,
        AzureOpenAiEmbeddingOptions options)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

        _options = options
            ?? throw new ArgumentNullException(nameof(options));

        ValidateOptions(_options);

        Descriptor = new TextEmbeddingProviderDescriptor(
            Provider: "AzureOpenAI",
            Model: _options.Model,
            Dimensions: _options.Dimensions,
            MaximumBatchSize: _options.MaximumBatchSize,
            MaximumInputCharacters: _options.MaximumInputCharacters);

        EmbeddingValidation.ValidateDescriptor(Descriptor);

        var endpoint = _options.Endpoint.AbsoluteUri.TrimEnd('/');

        var deployment = Uri.EscapeDataString(
            _options.Deployment);

        var apiVersion = Uri.EscapeDataString(
            _options.ApiVersion);

        _requestUri = new Uri(
            $"{endpoint}/openai/deployments/{deployment}" +
            $"/embeddings?api-version={apiVersion}",
            UriKind.Absolute);
    }

    /// <inheritdoc />
    public async Task<TextEmbeddingResult> GenerateAsync(
        TextEmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        EmbeddingValidation.ValidateRequest(
            request,
            Descriptor);

        var batchRequest = new TextEmbeddingBatchRequest(
            new[] { request });

        var batchResult = await GenerateBatchAsync(
            batchRequest,
            cancellationToken);

        return batchResult.Items[0];
    }

    /// <inheritdoc />
    public async Task<TextEmbeddingBatchResult> GenerateBatchAsync(
        TextEmbeddingBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        EmbeddingValidation.ValidateBatchRequest(
            request,
            Descriptor);

        cancellationToken.ThrowIfCancellationRequested();

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            _requestUri);

        httpRequest.Headers.Add(
            "api-key",
            _options.ApiKey);

        var payload = new
        {
            input = request.Items
                .Select(item => item.Text)
                .ToArray(),

            dimensions = _options.Dimensions
        };

        httpRequest.Content = JsonContent.Create(payload);

        HttpResponseMessage response;

        try
        {
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new TextEmbeddingProviderException(
                provider: Descriptor.Provider,
                message: "Azure OpenAI embedding request failed.",
                isTransient: true,
                innerException: exception);
        }
        catch (TaskCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TextEmbeddingProviderException(
                provider: Descriptor.Provider,
                message: "Azure OpenAI embedding request timed out.",
                isTransient: true,
                innerException: exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpException(response);
            }

            try
            {
                await using var responseStream =
                    await response.Content.ReadAsStreamAsync(
                        cancellationToken);

                using var document =
                    await JsonDocument.ParseAsync(
                        responseStream,
                        cancellationToken: cancellationToken);

                var result = ParseBatchResponse(
                    request,
                    document.RootElement);

                EmbeddingValidation.ValidateBatchResult(
                    request,
                    result,
                    Descriptor);

                return result;
            }
            catch (JsonException exception)
            {
                throw new TextEmbeddingProviderException(
                    provider: Descriptor.Provider,
                    message: "Azure OpenAI returned invalid embedding JSON.",
                    innerException: exception);
            }
        }
    }

    private TextEmbeddingBatchResult ParseBatchResponse(
        TextEmbeddingBatchRequest request,
        JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "Embedding response does not contain a valid data array.");
        }

        var expectedCount = request.Items.Count;

        if (data.GetArrayLength() != expectedCount)
        {
            throw new JsonException(
                "Embedding response count does not match request count.");
        }

        var results = new TextEmbeddingResult?[expectedCount];

        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("index", out var indexElement) ||
                !indexElement.TryGetInt32(out var index) ||
                index < 0 ||
                index >= expectedCount)
            {
                throw new JsonException(
                    "Embedding response contains an invalid index.");
            }

            if (results[index] is not null)
            {
                throw new JsonException(
                    "Embedding response contains duplicate indexes.");
            }

            if (!item.TryGetProperty(
                    "embedding",
                    out var embeddingElement) ||
                embeddingElement.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException(
                    "Embedding response contains an invalid vector.");
            }

            if (embeddingElement.GetArrayLength() !=
                Descriptor.Dimensions)
            {
                throw new JsonException(
                    "Embedding vector dimensions are incorrect.");
            }

            var vector = new float[Descriptor.Dimensions];

            var position = 0;

            foreach (var value in embeddingElement.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Number ||
                    !value.TryGetSingle(out var number))
                {
                    throw new JsonException(
                        "Embedding vector contains an invalid number.");
                }

                vector[position++] = number;
            }

            results[index] = new TextEmbeddingResult(
                RequestId: request.Items[index].RequestId,
                Vector: vector,
                Model: Descriptor.Model,
                Dimensions: Descriptor.Dimensions);
        }

        if (results.Any(item => item is null))
        {
            throw new JsonException(
                "Embedding response is missing one or more results.");
        }

        return new TextEmbeddingBatchResult(
            results.Select(item => item!).ToArray());
    }

    private TextEmbeddingProviderException CreateHttpException(
        HttpResponseMessage response)
    {
        var statusCode = response.StatusCode;

        var isTransient =
            statusCode == HttpStatusCode.RequestTimeout ||
            statusCode == HttpStatusCode.TooManyRequests ||
            (int)statusCode >= 500;

        var retryAfter = response.Headers.RetryAfter?.Delta;

        if (retryAfter is null &&
            response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            var delay = date - DateTimeOffset.UtcNow;

            retryAfter = delay > TimeSpan.Zero
                ? delay
                : TimeSpan.Zero;
        }

        return new TextEmbeddingProviderException(
            provider: Descriptor.Provider,
            message:
                "Azure OpenAI embedding request returned HTTP " +
                ((int)statusCode).ToString(
                    CultureInfo.InvariantCulture) +
                ".",
            isTransient: isTransient,
            retryAfter: retryAfter);
    }

    private static void ValidateOptions(
        AzureOpenAiEmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.Endpoint);

        if (!options.Endpoint.IsAbsoluteUri ||
            options.Endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Azure OpenAI endpoint must be an absolute HTTPS URI.",
                nameof(options));
        }

        if (!string.IsNullOrEmpty(options.Endpoint.Query) ||
            !string.IsNullOrEmpty(options.Endpoint.Fragment) ||
            options.Endpoint.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "Azure OpenAI endpoint must be a resource root URI.",
                nameof(options));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.Deployment);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.ApiKey);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.Model);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            options.ApiVersion);

        if (options.Deployment != options.Deployment.Trim() ||
            options.Model != options.Model.Trim() ||
            options.ApiVersion != options.ApiVersion.Trim())
        {
            throw new ArgumentException(
                "Azure OpenAI identifiers must be normalized.",
                nameof(options));
        }

        if (options.Deployment.Length > 128 ||
            options.Model.Length > 256 ||
            options.ApiVersion.Length > 64)
        {
            throw new ArgumentException(
                "Azure OpenAI configuration identifiers are too long.",
                nameof(options));
        }

        if (options.Deployment.Any(char.IsControl) ||
            options.ApiVersion.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Azure OpenAI configuration contains invalid characters.",
                nameof(options));
        }

        if (options.Dimensions <= 0 ||
            options.MaximumBatchSize <= 0 ||
            options.MaximumInputCharacters <= 0)
        {
            throw new ArgumentException(
                "Azure OpenAI embedding limits must be positive.",
                nameof(options));
        }
    }
}