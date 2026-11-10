using System.Buffers.Binary;

namespace EnterpriseDocumentActionAgent.Application.AI.Media;

/// <summary>
/// Represents structural metadata extracted
/// from a supported video container.
/// </summary>
/// <remarks>
/// Metadata inspection does not imply
/// successful video decoding.
///
/// Dimensions describe the selected video
/// track's declared presentation size.
///
/// Duration is read from the movie header.
/// </remarks>
public sealed record AiVideoMetadata
{
    /// <summary>
    /// Gets the media identifier.
    /// </summary>
    public string MediaId { get; }

    /// <summary>
    /// Gets the detected container format.
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// Gets the validated MIME type.
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Gets the declared video width.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the declared video height.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the declared pixel count.
    /// </summary>
    public long PixelCount =>
        (long)Width * Height;

    /// <summary>
    /// Gets the movie duration.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the input size in bytes.
    /// </summary>
    public int ContentLength { get; }

    /// <summary>
    /// Gets the inspection timestamp.
    /// </summary>
    public DateTimeOffset InspectedAtUtc { get; }

    /// <summary>
    /// Initializes video metadata.
    /// </summary>
    public AiVideoMetadata(
        string mediaId,
        string format,
        string mimeType,
        int width,
        int height,
        TimeSpan duration,
        int contentLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            mediaId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            format);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            mimeType);

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height));
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration));
        }

        if (contentLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentLength));
        }

        MediaId = mediaId.Trim();

        Format = format.Trim().ToUpperInvariant();

        MimeType = mimeType.Trim().ToLowerInvariant();

        Width = width;

        Height = height;

        Duration = duration;

        ContentLength = contentLength;

        InspectedAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Performs bounded structural inspection
/// of MP4 and QuickTime MOV containers.
/// </summary>
/// <remarks>
/// The processor reads ISO Base Media
/// File Format box headers.
///
/// It inspects:
/// - ftyp for container identification.
/// - moov/mvhd for movie duration.
/// - moov/trak/tkhd for track dimensions.
/// - moov/trak/mdia/hdlr for video tracks.
///
/// It does not decode video frames,
/// validate codecs, inspect media samples,
/// or scan for malware.
///
/// Additional isolation and decoding
/// controls are required for untrusted
/// video uploads.
/// </remarks>
public sealed class AiVideoProcessor : IAiMediaHandler
{
    /// <summary>
    /// Default maximum video size:
    /// 25 MiB.
    /// </summary>
    public const int DefaultMaxVideoBytes =
        25 * 1024 * 1024;

    /// <summary>
    /// Default maximum dimension.
    /// </summary>
    public const int DefaultMaxDimension = 16384;

    /// <summary>
    /// Default maximum pixel count.
    /// </summary>
    public const long DefaultMaxPixels =
        100_000_000;

    /// <summary>
    /// Default maximum duration.
    /// </summary>
    public static readonly TimeSpan DefaultMaxDuration =
        TimeSpan.FromHours(2);

    /// <inheritdoc />
    public AiMediaKind Kind => AiMediaKind.Video;

    /// <summary>
    /// Gets the maximum video size.
    /// </summary>
    public int MaxVideoBytes { get; }

    /// <summary>
    /// Gets the maximum dimension.
    /// </summary>
    public int MaxDimension { get; }

    /// <summary>
    /// Gets the maximum pixel count.
    /// </summary>
    public long MaxPixels { get; }

    /// <summary>
    /// Gets the maximum video duration.
    /// </summary>
    public TimeSpan MaxDuration { get; }

    /// <summary>
    /// Initializes a video processor.
    /// </summary>
    public AiVideoProcessor(
        int maxVideoBytes = DefaultMaxVideoBytes,
        int maxDimension = DefaultMaxDimension,
        long maxPixels = DefaultMaxPixels,
        TimeSpan? maxDuration = null)
    {
        if (maxVideoBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxVideoBytes));
        }

        if (maxDimension <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDimension));
        }

        if (maxPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPixels));
        }

        var durationLimit =
            maxDuration ?? DefaultMaxDuration;

        if (durationLimit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDuration));
        }

        MaxVideoBytes = maxVideoBytes;

        MaxDimension = maxDimension;

        MaxPixels = maxPixels;

        MaxDuration = durationLimit;
    }

    /// <summary>
    /// Inspects a supported video container.
    /// </summary>
    public Task<AiMediaProcessingResult> ProcessAsync(
        AiMediaInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        cancellationToken.ThrowIfCancellationRequested();

        if (input.Kind != AiMediaKind.Video)
        {
            return Failure(
                input,
                "The supplied media is not video.");
        }

        if (input.ContentLength > MaxVideoBytes)
        {
            return Failure(
                input,
                $"Video exceeds the configured limit " +
                $"of {MaxVideoBytes} bytes.");
        }

        if (!TryInspect(
            input.Content.Span,
            out var format,
            out var mimeType,
            out var width,
            out var height,
            out var duration,
            out var error))
        {
            return Failure(input, error);
        }

        if (!string.Equals(
            input.MimeType,
            mimeType,
            StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                input,
                $"Declared MIME type '{input.MimeType}' " +
                $"does not match detected type " +
                $"'{mimeType}'.");
        }

        if (width > MaxDimension ||
            height > MaxDimension)
        {
            return Failure(
                input,
                $"Video dimensions {width}x{height} " +
                $"exceed the configured dimension " +
                $"limit of {MaxDimension}.");
        }

        var pixelCount = (long)width * height;

        if (pixelCount > MaxPixels)
        {
            return Failure(
                input,
                $"Video pixel count {pixelCount} " +
                $"exceeds the configured limit " +
                $"of {MaxPixels}.");
        }

        if (duration > MaxDuration)
        {
            return Failure(
                input,
                $"Video duration {duration} " +
                $"exceeds the configured limit " +
                $"of {MaxDuration}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var metadata = new AiVideoMetadata(
            mediaId: input.MediaId,
            format: format,
            mimeType: mimeType,
            width: width,
            height: height,
            duration: duration,
            contentLength: input.ContentLength);

        return Task.FromResult(
            AiMediaProcessingResult.Success(
                input,
                processedContent: metadata,
                message:
                    "Video container metadata inspected."));
    }

    private static Task<AiMediaProcessingResult> Failure(
        AiMediaInput input,
        string message)
    {
        return Task.FromResult(
            AiMediaProcessingResult.Failure(
                input,
                message));
    }

    /// <summary>
    /// Inspects the video container and
    /// extracts movie and track metadata.
    /// </summary>
    private static bool TryInspect(
        ReadOnlySpan<byte> content,
        out string format,
        out string mimeType,
        out int width,
        out int height,
        out TimeSpan duration,
        out string error)
    {
        format = string.Empty;
        mimeType = string.Empty;
        width = 0;
        height = 0;
        duration = TimeSpan.Zero;
        error = string.Empty;

        if (!TryReadBox(
            content,
            0,
            content.Length,
            out var ftyp))
        {
            error = "Video container header is invalid.";
            return false;
        }

        if (!BoxTypeEquals(content, ftyp, "ftyp"u8))
        {
            error = "Video ftyp box is missing.";
            return false;
        }

        if (ftyp.PayloadLength < 8)
        {
            error = "Video ftyp box is incomplete.";
            return false;
        }

        var brand = content.Slice(
            ftyp.PayloadOffset,
            4);

        if (brand.SequenceEqual("qt  "u8))
        {
            format = "MOV";
            mimeType = "video/quicktime";
        }
        else if (IsSupportedMp4Brand(brand))
        {
            format = "MP4";
            mimeType = "video/mp4";
        }
        else
        {
            error = "Unsupported video container brand.";
            return false;
        }

        var offset = ftyp.EndOffset;
        var foundMovie = false;

        while (offset < content.Length)
        {
            if (!TryReadBox(
                content,
                offset,
                content.Length,
                out var box))
            {
                error = "Invalid top-level video box.";
                return false;
            }

            if (BoxTypeEquals(content, box, "moov"u8))
            {
                if (!TryReadMovie(
                    content,
                    box,
                    out width,
                    out height,
                    out duration,
                    out error))
                {
                    return false;
                }

                foundMovie = true;
                break;
            }

            offset = box.EndOffset;
        }

        if (!foundMovie)
        {
            error = "Video moov box is missing.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether a brand is
    /// supported as an MP4-family container.
    /// </summary>
    private static bool IsSupportedMp4Brand(
        ReadOnlySpan<byte> brand)
    {
        return brand.SequenceEqual("isom"u8) ||
               brand.SequenceEqual("iso2"u8) ||
               brand.SequenceEqual("mp41"u8) ||
               brand.SequenceEqual("mp42"u8) ||
               brand.SequenceEqual("avc1"u8) ||
               brand.SequenceEqual("M4V "u8);
    }

    /// <summary>
    /// Reads movie duration and selects
    /// the first valid video track.
    /// </summary>
    private static bool TryReadMovie(
        ReadOnlySpan<byte> content,
        Box movie,
        out int width,
        out int height,
        out TimeSpan duration,
        out string error)
    {
        width = 0;
        height = 0;
        duration = TimeSpan.Zero;
        error = string.Empty;

        var offset = movie.PayloadOffset;
        var end = movie.EndOffset;

        var foundHeader = false;
        var foundVideoTrack = false;

        while (offset < end)
        {
            if (!TryReadBox(
                content,
                offset,
                end,
                out var child))
            {
                error = "Invalid movie metadata box.";
                return false;
            }

            if (BoxTypeEquals(content, child, "mvhd"u8))
            {
                if (!TryReadMovieHeader(
                    content,
                    child,
                    out duration,
                    out error))
                {
                    return false;
                }

                foundHeader = true;
            }
            else if (
                BoxTypeEquals(content, child, "trak"u8) &&
                !foundVideoTrack)
            {
                if (!TryReadTrack(
                    content,
                    child,
                    out var trackWidth,
                    out var trackHeight,
                    out var isVideo,
                    out error))
                {
                    return false;
                }

                if (isVideo)
                {
                    width = trackWidth;
                    height = trackHeight;
                    foundVideoTrack = true;
                }
            }

            offset = child.EndOffset;
        }

        if (!foundHeader)
        {
            error = "Movie header is missing.";
            return false;
        }

        if (!foundVideoTrack)
        {
            error = "Video track metadata is missing.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads mvhd version 0 or 1.
    /// </summary>
    private static bool TryReadMovieHeader(
        ReadOnlySpan<byte> content,
        Box box,
        out TimeSpan duration,
        out string error)
    {
        duration = TimeSpan.Zero;
        error = string.Empty;

        if (box.PayloadLength < 20)
        {
            error = "Movie header is incomplete.";
            return false;
        }

        var payload = content.Slice(
            box.PayloadOffset,
            box.PayloadLength);

        var version = payload[0];

        uint timescale;
        ulong durationUnits;

        if (version == 0)
        {
            if (payload.Length < 20)
            {
                error = "Movie header version 0 is incomplete.";
                return false;
            }

            timescale =
                BinaryPrimitives.ReadUInt32BigEndian(
                    payload.Slice(12, 4));

            durationUnits =
                BinaryPrimitives.ReadUInt32BigEndian(
                    payload.Slice(16, 4));
        }
        else if (version == 1)
        {
            if (payload.Length < 32)
            {
                error = "Movie header version 1 is incomplete.";
                return false;
            }

            timescale =
                BinaryPrimitives.ReadUInt32BigEndian(
                    payload.Slice(20, 4));

            durationUnits =
                BinaryPrimitives.ReadUInt64BigEndian(
                    payload.Slice(24, 8));
        }
        else
        {
            error = "Unsupported movie header version.";
            return false;
        }

        if (timescale == 0 || durationUnits == 0)
        {
            error = "Movie duration metadata is invalid.";
            return false;
        }

        var seconds = (double)durationUnits / timescale;

        if (!double.IsFinite(seconds) ||
            seconds <= 0 ||
            seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            error = "Movie duration is out of range.";
            return false;
        }

        duration = TimeSpan.FromSeconds(seconds);

        return true;
    }

    /// <summary>
    /// Reads a track header and determines
    /// whether the track contains video.
    /// </summary>
    private static bool TryReadTrack(
        ReadOnlySpan<byte> content,
        Box track,
        out int width,
        out int height,
        out bool isVideo,
        out string error)
    {
        width = 0;
        height = 0;
        isVideo = false;
        error = string.Empty;

        var offset = track.PayloadOffset;
        var end = track.EndOffset;

        var foundTrackHeader = false;
        var foundMediaHandler = false;

        while (offset < end)
        {
            if (!TryReadBox(
                content,
                offset,
                end,
                out var child))
            {
                error = "Invalid track metadata box.";
                return false;
            }

            if (BoxTypeEquals(content, child, "tkhd"u8))
            {
                if (!TryReadTrackHeader(
                    content,
                    child,
                    out width,
                    out height,
                    out error))
                {
                    return false;
                }

                foundTrackHeader = true;
            }
            else if (BoxTypeEquals(
                content,
                child,
                "mdia"u8))
            {
                if (!TryReadMediaHandler(
                    content,
                    child,
                    out isVideo,
                    out error))
                {
                    return false;
                }

                foundMediaHandler = true;
            }

            offset = child.EndOffset;
        }

        if (!foundTrackHeader || !foundMediaHandler)
        {
            error = "Track metadata is incomplete.";
            return false;
        }

        if (isVideo && (width <= 0 || height <= 0))
        {
            error = "Video track dimensions are invalid.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads fixed-point track dimensions.
    /// </summary>
    private static bool TryReadTrackHeader(
        ReadOnlySpan<byte> content,
        Box box,
        out int width,
        out int height,
        out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        var payload = content.Slice(
            box.PayloadOffset,
            box.PayloadLength);

        if (payload.Length < 4)
        {
            error = "Track header is incomplete.";
            return false;
        }

        var version = payload[0];

        var widthOffset = version switch
        {
            0 => 76,
            1 => 88,
            _ => -1
        };

        if (widthOffset < 0 ||
            payload.Length < widthOffset + 8)
        {
            error = "Unsupported or incomplete track header.";
            return false;
        }

        var rawWidth =
            BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(widthOffset, 4));

        var rawHeight =
            BinaryPrimitives.ReadUInt32BigEndian(
                payload.Slice(widthOffset + 4, 4));

        width = (int)(rawWidth >> 16);
        height = (int)(rawHeight >> 16);

        return true;
    }

    /// <summary>
    /// Reads the media handler type.
    /// </summary>
    private static bool TryReadMediaHandler(
        ReadOnlySpan<byte> content,
        Box media,
        out bool isVideo,
        out string error)
    {
        isVideo = false;
        error = string.Empty;

        var offset = media.PayloadOffset;
        var end = media.EndOffset;
        var foundHandler = false;

        while (offset < end)
        {
            if (!TryReadBox(
                content,
                offset,
                end,
                out var child))
            {
                error = "Invalid media metadata box.";
                return false;
            }

            if (BoxTypeEquals(content, child, "hdlr"u8))
            {
                var payload = content.Slice(
                    child.PayloadOffset,
                    child.PayloadLength);

                if (payload.Length < 12)
                {
                    error = "Media handler box is incomplete.";
                    return false;
                }

                isVideo = payload
                    .Slice(8, 4)
                    .SequenceEqual("vide"u8);

                foundHandler = true;
                break;
            }

            offset = child.EndOffset;
        }

        if (!foundHandler)
        {
            error = "Media handler metadata is missing.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads a bounded ISO media box header.
    /// </summary>
    private static bool TryReadBox(
        ReadOnlySpan<byte> content,
        int offset,
        int limit,
        out Box box)
    {
        box = default;

        if (offset < 0 ||
            limit > content.Length ||
            offset > limit - 8)
        {
            return false;
        }

        var size32 =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Slice(offset, 4));

        var headerLength = 8;
        long boxLength = size32;

        if (size32 == 1)
        {
            if (offset > limit - 16)
            {
                return false;
            }

            boxLength =
                (long)BinaryPrimitives.ReadUInt64BigEndian(
                    content.Slice(offset + 8, 8));

            headerLength = 16;
        }
        else if (size32 == 0)
        {
            boxLength = limit - offset;
        }

        if (boxLength < headerLength ||
            boxLength > limit - offset)
        {
            return false;
        }

        var endOffset = offset + (int)boxLength;

        box = new Box(
            TypeOffset: offset + 4,
            PayloadOffset: offset + headerLength,
            PayloadLength: (int)boxLength - headerLength,
            EndOffset: endOffset);

        return true;
    }

    /// <summary>
    /// Compares a box's four-byte type.
    /// </summary>
    private static bool BoxTypeEquals(
        ReadOnlySpan<byte> content,
        Box box,
        ReadOnlySpan<byte> expected)
    {
        return content
            .Slice(box.TypeOffset, 4)
            .SequenceEqual(expected);
    }

    /// <summary>
    /// Represents a validated box boundary.
    /// </summary>
    private readonly record struct Box(
        int TypeOffset,
        int PayloadOffset,
        int PayloadLength,
        int EndOffset);
}