using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnterpriseDocumentActionAgent.Application.AI.Pipelines;

/// <summary>
/// Parses JSON-formatted AI model output into
/// a strongly typed .NET object.
/// </summary>
/// <typeparam name="TOutput">
/// Type produced by JSON deserialization.
/// </typeparam>
/// <remarks>
/// The parser accepts a complete JSON document.
///
/// Markdown code fences, explanatory text,
/// and partially generated JSON are not
/// automatically removed or repaired.
///
/// JSON parsing does not establish factual
/// correctness or business-rule validity.
/// </remarks>
public sealed class AiStructuredOutputParser<TOutput>
    : IAiRunnable<string, TOutput>
{
    private readonly JsonSerializerOptions _serializerOptions;

    private readonly int _maxInputCharacters;

    private readonly JsonValueKind? _requiredRootKind;

    /// <summary>
    /// Gets the maximum accepted input length.
    /// </summary>
    public int MaxInputCharacters => _maxInputCharacters;

    /// <summary>
    /// Gets the required JSON root kind,
    /// or null when no restriction applies.
    /// </summary>
    public JsonValueKind? RequiredRootKind =>
        _requiredRootKind;

    /// <summary>
    /// Initializes a structured output parser.
    /// </summary>
    /// <param name="serializerOptions">
    /// Optional JSON serialization configuration.
    /// </param>
    /// <param name="maxInputCharacters">
    /// Maximum number of input characters.
    /// </param>
    /// <param name="requiredRootKind">
    /// Optional required JSON root kind.
    /// </param>
    public AiStructuredOutputParser(
        JsonSerializerOptions? serializerOptions = null,
        int maxInputCharacters = 1_000_000,
        JsonValueKind? requiredRootKind = JsonValueKind.Object)
    {
        if (maxInputCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxInputCharacters),
                "Maximum input length must be positive.");
        }

        if (requiredRootKind is JsonValueKind.Undefined)
        {
            throw new ArgumentException(
                "Undefined is not a valid required root kind.",
                nameof(requiredRootKind));
        }

        _maxInputCharacters = maxInputCharacters;

        _requiredRootKind = requiredRootKind;

        _serializerOptions = serializerOptions is null
            ? CreateDefaultOptions()
            : new JsonSerializerOptions(serializerOptions);
    }

    /// <summary>
    /// Parses JSON text into the configured
    /// output type.
    /// </summary>
    /// <param name="json">
    /// JSON text to deserialize.
    /// </param>
    /// <returns>
    /// Deserialized output object.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when input is empty or too large.
    /// </exception>
    /// <exception cref="JsonException">
    /// Thrown when JSON is invalid, has an
    /// unexpected root kind, or cannot be
    /// deserialized into the target type.
    /// </exception>
    public TOutput Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        if (json.Length > _maxInputCharacters)
        {
            throw new ArgumentException(
                $"JSON input exceeds the configured limit " +
                $"of {_maxInputCharacters} characters.",
                nameof(json));
        }

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = _serializerOptions.MaxDepth
            });

        if (_requiredRootKind.HasValue &&
            document.RootElement.ValueKind !=
            _requiredRootKind.Value)
        {
            throw new JsonException(
                $"Expected JSON root kind " +
                $"'{_requiredRootKind.Value}', but received " +
                $"'{document.RootElement.ValueKind}'.");
        }

        var result = document.RootElement.Deserialize<TOutput>(
            _serializerOptions);

        if (result is null)
        {
            throw new JsonException(
                "JSON deserialization produced a null result.");
        }

        return result;
    }

    /// <summary>
    /// Executes parsing as an asynchronous
    /// AI pipeline operation.
    /// </summary>
    public Task<TOutput> RunAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = Parse(input);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(result);
    }

    /// <summary>
    /// Attempts to parse JSON without propagating
    /// expected input-validation failures.
    /// </summary>
    /// <remarks>
    /// Unexpected serializer or converter exceptions
    /// are not suppressed.
    /// </remarks>
    public bool TryParse(
        string? json,
        out TOutput? result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            result = Parse(json);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates default JSON serializer options
    /// for structured AI model responses.
    /// </summary>
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling =
                JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling =
                JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = 64
        };
    }
}

/// <summary>
/// Provides factory methods for structured
/// AI output parsers.
/// </summary>
public static class AiStructuredOutputParser
{
    /// <summary>
    /// Creates a parser for a JSON object.
    /// </summary>
    public static AiStructuredOutputParser<TOutput>
        ForObject<TOutput>(
            JsonSerializerOptions? serializerOptions = null,
            int maxInputCharacters = 1_000_000)
    {
        return new AiStructuredOutputParser<TOutput>(
            serializerOptions,
            maxInputCharacters,
            JsonValueKind.Object);
    }

    /// <summary>
    /// Creates a parser for a JSON array.
    /// </summary>
    public static AiStructuredOutputParser<TOutput>
        ForArray<TOutput>(
            JsonSerializerOptions? serializerOptions = null,
            int maxInputCharacters = 1_000_000)
    {
        return new AiStructuredOutputParser<TOutput>(
            serializerOptions,
            maxInputCharacters,
            JsonValueKind.Array);
    }

    /// <summary>
    /// Creates a parser without restricting
    /// the JSON root kind.
    /// </summary>
    public static AiStructuredOutputParser<TOutput>
        ForAnyJson<TOutput>(
            JsonSerializerOptions? serializerOptions = null,
            int maxInputCharacters = 1_000_000)
    {
        return new AiStructuredOutputParser<TOutput>(
            serializerOptions,
            maxInputCharacters,
            requiredRootKind: null);
    }
}