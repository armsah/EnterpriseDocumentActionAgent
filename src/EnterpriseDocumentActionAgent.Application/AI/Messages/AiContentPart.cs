namespace EnterpriseDocumentActionAgent.Application.AI.Messages;

/// <summary>
/// Identifies the modality of an AI content part.
/// </summary>
public enum AiContentKind
{
    Text = 1,
    Image = 2,
    Audio = 3,
    Video = 4,
    Document = 5,
    StructuredData = 6
}

/// <summary>
/// Represents immutable, provider-neutral AI content.
/// </summary>
/// <remarks>
/// Media content is represented by a resource reference.
///
/// A resource URI does not establish authorization,
/// resource availability, or content safety.
///
/// Providers must resolve media through an authorized
/// resource-access component.
/// </remarks>
public sealed record AiContentPart
{
    public const int MaximumTextCharacters = 64_000;

    public const int MaximumMimeTypeCharacters = 200;

    public const int MaximumResourceUriCharacters = 2_048;

    public AiContentKind Kind { get; }

    public string? Text { get; }

    public string? MediaType { get; }

    public Uri? ResourceUri { get; }

    public bool IsMedia =>
        Kind is AiContentKind.Image
            or AiContentKind.Audio
            or AiContentKind.Video
            or AiContentKind.Document;

    public bool IsTextual =>
        Kind is AiContentKind.Text
            or AiContentKind.StructuredData;

    private AiContentPart(
        AiContentKind kind,
        string? text,
        string? mediaType,
        Uri? resourceUri)
    {
        Kind = kind;
        Text = text;
        MediaType = mediaType;
        ResourceUri = resourceUri;
    }

    /// <summary>
    /// Creates a text content part.
    /// </summary>
    public static AiContentPart FromText(string text)
    {
        ValidateText(text, nameof(text));

        return new AiContentPart(
            AiContentKind.Text,
            text,
            "text/plain",
            null);
    }

    /// <summary>
    /// Creates structured JSON content.
    /// </summary>
    public static AiContentPart FromJson(string json)
    {
        ValidateText(json, nameof(json));

        try
        {
            using var document =
                System.Text.Json.JsonDocument.Parse(json);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ArgumentException(
                "Structured content must contain valid JSON.",
                nameof(json),
                exception);
        }

        return new AiContentPart(
            AiContentKind.StructuredData,
            json,
            "application/json",
            null);
    }

    /// <summary>
    /// Creates an image reference.
    /// </summary>
    public static AiContentPart FromImage(
        Uri resourceUri,
        string mediaType)
    {
        return CreateMedia(
            AiContentKind.Image,
            resourceUri,
            mediaType,
            "image/");
    }

    /// <summary>
    /// Creates an audio reference.
    /// </summary>
    public static AiContentPart FromAudio(
        Uri resourceUri,
        string mediaType)
    {
        return CreateMedia(
            AiContentKind.Audio,
            resourceUri,
            mediaType,
            "audio/");
    }

    /// <summary>
    /// Creates a video reference.
    /// </summary>
    public static AiContentPart FromVideo(
        Uri resourceUri,
        string mediaType)
    {
        return CreateMedia(
            AiContentKind.Video,
            resourceUri,
            mediaType,
            "video/");
    }

    /// <summary>
    /// Creates a document reference.
    /// </summary>
    public static AiContentPart FromDocument(
        Uri resourceUri,
        string mediaType)
    {
        return CreateMedia(
            AiContentKind.Document,
            resourceUri,
            mediaType,
            null);
    }

    private static AiContentPart CreateMedia(
        AiContentKind kind,
        Uri resourceUri,
        string mediaType,
        string? requiredMediaTypePrefix)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);

        if (!resourceUri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "Media resource URI must be absolute.",
                nameof(resourceUri));
        }

        if (resourceUri.OriginalString.Length >
            MaximumResourceUriCharacters)
        {
            throw new ArgumentException(
                "Media resource URI exceeds the allowed length.",
                nameof(resourceUri));
        }

        if (!string.Equals(
                resourceUri.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Only HTTPS media references are supported.",
                nameof(resourceUri));
        }

        if (!string.IsNullOrEmpty(resourceUri.UserInfo))
        {
            throw new ArgumentException(
                "Media resource URI cannot contain user information.",
                nameof(resourceUri));
        }

        if (!string.IsNullOrEmpty(resourceUri.Fragment))
        {
            throw new ArgumentException(
                "Media resource URI cannot contain a fragment.",
                nameof(resourceUri));
        }

        ValidateMediaType(
            mediaType,
            requiredMediaTypePrefix);

        return new AiContentPart(
            kind,
            null,
            mediaType,
            resourceUri);
    }

    private static void ValidateText(
        string? text,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Content text cannot be empty.",
                parameterName);
        }

        if (text.Length > MaximumTextCharacters)
        {
            throw new ArgumentException(
                $"Content text cannot exceed " +
                $"{MaximumTextCharacters} characters.",
                parameterName);
        }
    }

    private static void ValidateMediaType(
        string? mediaType,
        string? requiredPrefix)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            throw new ArgumentException(
                "Media type is required.",
                nameof(mediaType));
        }

        if (mediaType.Length > MaximumMimeTypeCharacters)
        {
            throw new ArgumentException(
                "Media type exceeds the allowed length.",
                nameof(mediaType));
        }

        if (mediaType.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Media type cannot contain whitespace.",
                nameof(mediaType));
        }

        var separatorIndex = mediaType.IndexOf('/');

        if (separatorIndex <= 0 ||
            separatorIndex == mediaType.Length - 1 ||
            mediaType.IndexOf('/', separatorIndex + 1) >= 0)
        {
            throw new ArgumentException(
                "Media type must use the type/subtype format.",
                nameof(mediaType));
        }

        if (requiredPrefix is not null &&
            !mediaType.StartsWith(
                requiredPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Media type must begin with '{requiredPrefix}'.",
                nameof(mediaType));
        }
    }
}



/*
using EnterpriseDocumentActionAgent.Application.AI.Messages;

var text = AiContentPart.FromText(
    "Explain the information in this image.");

var image = AiContentPart.FromImage(
    new Uri("https://example.com/images/chart.png"),
    "image/png");

var audio = AiContentPart.FromAudio(
    new Uri("https://example.com/audio/meeting.wav"),
    "audio/wav");

var video = AiContentPart.FromVideo(
    new Uri("https://example.com/video/demo.mp4"),
    "video/mp4");

var document = AiContentPart.FromDocument(
    new Uri("https://example.com/documents/report.pdf"),
    "application/pdf");

var structuredData = AiContentPart.FromJson(
    """{"task":"summarize","priority":"high"}""");
    */