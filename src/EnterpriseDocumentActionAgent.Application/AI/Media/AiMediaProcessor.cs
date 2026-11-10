using System.Collections.Concurrent;

namespace EnterpriseDocumentActionAgent.Application.AI.Media;

/// <summary>
/// Identifies the category of an AI media input.
/// </summary>
public enum AiMediaKind
{
    Image = 1,
    Audio = 2,
    Video = 3
}

/// <summary>
/// Represents a validated media input.
/// </summary>
/// <remarks>
/// Binary content is copied during construction
/// and is exposed through read-only memory.
///
/// This prevents callers from modifying the
/// original input array after construction.
///
/// MIME type declarations are metadata only.
/// They do not prove the actual file format.
/// </remarks>
public sealed class AiMediaInput
{
    private readonly byte[] _content;

    /// <summary>
    /// Gets the unique media identifier.
    /// </summary>
    public string MediaId { get; }

    /// <summary>
    /// Gets the media category.
    /// </summary>
    public AiMediaKind Kind { get; }

    /// <summary>
    /// Gets the declared MIME type.
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Gets the optional source reference.
    /// </summary>
    public string? SourceReference { get; }

    /// <summary>
    /// Gets the media size in bytes.
    /// </summary>
    public int ContentLength => _content.Length;

    /// <summary>
    /// Gets the binary media content.
    /// </summary>
    public ReadOnlyMemory<byte> Content => _content;

    /// <summary>
    /// Gets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Initializes a media input.
    /// </summary>
    public AiMediaInput(
        string mediaId,
        AiMediaKind kind,
        string mimeType,
        ReadOnlyMemory<byte> content,
        string? sourceReference = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);

        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind));
        }

        if (content.IsEmpty)
        {
            throw new ArgumentException(
                "Media content cannot be empty.",
                nameof(content));
        }

        MediaId = mediaId.Trim();

        Kind = kind;

        MimeType = mimeType.Trim().ToLowerInvariant();

        SourceReference =
            string.IsNullOrWhiteSpace(sourceReference)
                ? null
                : sourceReference.Trim();

        _content = content.ToArray();

        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Returns an independent copy of the
    /// binary media content.
    /// </summary>
    public byte[] GetContentCopy()
    {
        return (byte[])_content.Clone();
    }
}

/// <summary>
/// Represents the result of media processing.
/// </summary>
/// <remarks>
/// ProcessedContent is application-defined.
///
/// It may contain transformed binary data,
/// extracted metadata, or another result
/// produced by a specialized handler.
///
/// This result does not guarantee that
/// an AI model has analyzed the media.
/// </remarks>
public sealed record AiMediaProcessingResult
{
    /// <summary>
    /// Gets the media identifier.
    /// </summary>
    public string MediaId { get; }

    /// <summary>
    /// Gets the media category.
    /// </summary>
    public AiMediaKind Kind { get; }

    /// <summary>
    /// Gets the MIME type.
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Gets whether processing succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the processed output.
    /// </summary>
    public object? ProcessedContent { get; }

    /// <summary>
    /// Gets an optional processing message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the completion timestamp.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Initializes a media processing result.
    /// </summary>
    public AiMediaProcessingResult(
        string mediaId,
        AiMediaKind kind,
        string mimeType,
        bool succeeded,
        object? processedContent = null,
        string? message = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);

        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind));
        }

        MediaId = mediaId.Trim();

        Kind = kind;

        MimeType = mimeType.Trim().ToLowerInvariant();

        Succeeded = succeeded;

        ProcessedContent = processedContent;

        Message = string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim();

        CompletedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Creates a successful processing result.
    /// </summary>
    public static AiMediaProcessingResult Success(
        AiMediaInput input,
        object? processedContent = null,
        string? message = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        return new AiMediaProcessingResult(
            input.MediaId,
            input.Kind,
            input.MimeType,
            succeeded: true,
            processedContent: processedContent,
            message: message);
    }

    /// <summary>
    /// Creates a failed processing result.
    /// </summary>
    public static AiMediaProcessingResult Failure(
        AiMediaInput input,
        string message)
    {
        ArgumentNullException.ThrowIfNull(input);

        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new AiMediaProcessingResult(
            input.MediaId,
            input.Kind,
            input.MimeType,
            succeeded: false,
            message: message);
    }
}

/// <summary>
/// Defines a specialized media processor.
/// </summary>
/// <remarks>
/// Implementations are responsible for
/// validating media format details and
/// performing media-specific operations.
/// </remarks>
public interface IAiMediaHandler
{
    /// <summary>
    /// Gets the supported media category.
    /// </summary>
    AiMediaKind Kind { get; }

    /// <summary>
    /// Processes a media input.
    /// </summary>
    Task<AiMediaProcessingResult> ProcessAsync(
        AiMediaInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Dispatches media inputs to registered
/// specialized media handlers.
/// </summary>
/// <remarks>
/// Handler registration is thread-safe.
///
/// Each media category supports one
/// registered handler.
///
/// The processor validates declared
/// media categories and size limits.
///
/// It does not inspect file signatures,
/// scan for malware, or authenticate
/// media sources.
///
/// Specialized handlers must enforce
/// additional format-specific checks.
/// </remarks>
public sealed class AiMediaProcessor
{
    /// <summary>
    /// Default maximum media size:
    /// 25 MiB.
    /// </summary>
    public const int DefaultMaxMediaBytes =
        25 * 1024 * 1024;

    private readonly ConcurrentDictionary<
        AiMediaKind,
        IAiMediaHandler> _handlers = new();

    /// <summary>
    /// Gets the configured maximum media size.
    /// </summary>
    public int MaxMediaBytes { get; }

    /// <summary>
    /// Gets the number of registered handlers.
    /// </summary>
    public int HandlerCount => _handlers.Count;

    /// <summary>
    /// Initializes a media processor.
    /// </summary>
    public AiMediaProcessor(
        int maxMediaBytes = DefaultMaxMediaBytes)
    {
        if (maxMediaBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxMediaBytes));
        }

        MaxMediaBytes = maxMediaBytes;
    }

    /// <summary>
    /// Registers a specialized media handler.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a handler is already
    /// registered for the same media kind.
    /// </exception>
    public AiMediaProcessor RegisterHandler(
        IAiMediaHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (!Enum.IsDefined(handler.Kind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(handler),
                "Handler media kind is invalid.");
        }

        if (!_handlers.TryAdd(handler.Kind, handler))
        {
            throw new InvalidOperationException(
                $"A media handler for '{handler.Kind}' " +
                "is already registered.");
        }

        return this;
    }

    /// <summary>
    /// Gets whether a media handler is registered.
    /// </summary>
    public bool ContainsHandler(
        AiMediaKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            return false;
        }

        return _handlers.ContainsKey(kind);
    }

    /// <summary>
    /// Removes a registered media handler.
    /// </summary>
    public bool UnregisterHandler(
        AiMediaKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            return false;
        }

        return _handlers.TryRemove(kind, out _);
    }

    /// <summary>
    /// Gets the supported media categories.
    /// </summary>
    public IReadOnlyList<AiMediaKind> GetSupportedKinds()
    {
        return Array.AsReadOnly(
            _handlers.Keys
                .OrderBy(kind => kind)
                .ToArray());
    }

    /// <summary>
    /// Processes media using the registered
    /// handler for its category.
    /// </summary>
    public async Task<AiMediaProcessingResult> ProcessAsync(
        AiMediaInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        cancellationToken.ThrowIfCancellationRequested();

        if (input.ContentLength > MaxMediaBytes)
        {
            throw new InvalidOperationException(
                $"Media '{input.MediaId}' exceeds " +
                $"the configured limit of " +
                $"{MaxMediaBytes} bytes.");
        }

        if (!_handlers.TryGetValue(
            input.Kind,
            out var handler))
        {
            throw new InvalidOperationException(
                $"No media handler is registered " +
                $"for category '{input.Kind}'.");
        }

        var result = await handler.ProcessAsync(
            input,
            cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (result is null)
        {
            throw new InvalidOperationException(
                $"Media handler for '{input.Kind}' " +
                "returned a null result.");
        }

        if (!string.Equals(
                result.MediaId,
                input.MediaId,
                StringComparison.Ordinal) ||
            result.Kind != input.Kind)
        {
            throw new InvalidOperationException(
                "Media handler returned a result " +
                "for a different media input.");
        }

        return result;
    }
}