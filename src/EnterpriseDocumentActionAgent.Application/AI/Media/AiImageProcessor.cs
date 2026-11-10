using System.Buffers.Binary;

namespace EnterpriseDocumentActionAgent.Application.AI.Media;

/// <summary>
/// Represents validated structural metadata
/// extracted from an image.
/// </summary>
/// <remarks>
/// Metadata inspection does not imply
/// successful full image decoding.
///
/// The image is not analyzed by an AI model.
/// </remarks>
public sealed record AiImageMetadata
{
    /// <summary>
    /// Gets the image identifier.
    /// </summary>
    public string MediaId { get; }

    /// <summary>
    /// Gets the detected image format.
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// Gets the validated MIME type.
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Gets the image width.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the image height.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the pixel count.
    /// </summary>
    public long PixelCount =>
        (long)Width * Height;

    /// <summary>
    /// Gets the source size in bytes.
    /// </summary>
    public int ContentLength { get; }

    /// <summary>
    /// Gets the inspection timestamp.
    /// </summary>
    public DateTimeOffset InspectedAtUtc { get; }

    /// <summary>
    /// Initializes image metadata.
    /// </summary>
    public AiImageMetadata(
        string mediaId,
        string format,
        string mimeType,
        int width,
        int height,
        int contentLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);

        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

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

        ContentLength = contentLength;

        InspectedAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Performs structural inspection of
/// supported image media.
/// </summary>
/// <remarks>
/// Supported formats:
///
/// PNG:
///   Validates the PNG signature and
///   reads dimensions from the IHDR chunk.
///
/// JPEG:
///   Inspects JPEG marker segments until
///   a supported start-of-frame marker
///   provides image dimensions.
///
/// GIF:
///   Validates the GIF header and reads
///   logical screen dimensions.
///
/// The processor does not decode pixels,
/// inspect embedded scripts, scan for
/// malware, or perform image recognition.
///
/// For untrusted uploads, additional
/// security controls and a hardened
/// image decoder are required.
/// </remarks>
public sealed class AiImageProcessor : IAiMediaHandler
{
    /// <summary>
    /// Default maximum image size:
    /// 20 MiB.
    /// </summary>
    public const int DefaultMaxImageBytes =
        20 * 1024 * 1024;

    /// <summary>
    /// Default maximum image dimension.
    /// </summary>
    public const int DefaultMaxDimension = 16384;

    /// <summary>
    /// Default maximum pixel count.
    /// </summary>
    public const long DefaultMaxPixels =
        100_000_000;

    /// <inheritdoc />
    public AiMediaKind Kind => AiMediaKind.Image;

    /// <summary>
    /// Gets the maximum image size.
    /// </summary>
    public int MaxImageBytes { get; }

    /// <summary>
    /// Gets the maximum width or height.
    /// </summary>
    public int MaxDimension { get; }

    /// <summary>
    /// Gets the maximum pixel count.
    /// </summary>
    public long MaxPixels { get; }

    /// <summary>
    /// Initializes an image processor.
    /// </summary>
    public AiImageProcessor(
        int maxImageBytes = DefaultMaxImageBytes,
        int maxDimension = DefaultMaxDimension,
        long maxPixels = DefaultMaxPixels)
    {
        if (maxImageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxImageBytes));
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

        MaxImageBytes = maxImageBytes;

        MaxDimension = maxDimension;

        MaxPixels = maxPixels;
    }

    /// <summary>
    /// Inspects an image and returns
    /// structured processing metadata.
    /// </summary>
    public Task<AiMediaProcessingResult> ProcessAsync(
        AiMediaInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        cancellationToken.ThrowIfCancellationRequested();

        if (input.Kind != AiMediaKind.Image)
        {
            return Task.FromResult(
                AiMediaProcessingResult.Failure(
                    input,
                    "The supplied media is not an image."));
        }

        if (input.ContentLength > MaxImageBytes)
        {
            return Task.FromResult(
                AiMediaProcessingResult.Failure(
                    input,
                    $"Image exceeds the configured limit " +
                    $"of {MaxImageBytes} bytes."));
        }

        var content = input.Content.Span;

        if (!TryInspectImage(
            content,
            out var format,
            out var mimeType,
            out var width,
            out var height,
            out var error))
        {
            return Task.FromResult(
                AiMediaProcessingResult.Failure(
                    input,
                    error));
        }

        if (!string.Equals(
            input.MimeType,
            mimeType,
            StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                AiMediaProcessingResult.Failure(
                    input,
                    $"Declared MIME type '{input.MimeType}' " +
                    $"does not match detected type " +
                    $"'{mimeType}'."));
        }

        if (width > MaxDimension ||
            height > MaxDimension)
        {
            return Task.FromResult(
                AiMediaProcessingResult.Failure(
                    input,
                    $"Image dimensions {width}x{height} " +
                    $"exceed the maximum dimension " +
                    $"of {MaxDimension}."));
        }

        var pixelCount = (long)width * height;

        if (pixelCount > MaxPixels)
        {
            return Task.FromResult(
                AiMediaProcessingResult.Failure(
                    input,
                    $"Image pixel count {pixelCount} " +
                    $"exceeds the configured limit " +
                    $"of {MaxPixels}."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var metadata = new AiImageMetadata(
            mediaId: input.MediaId,
            format: format,
            mimeType: mimeType,
            width: width,
            height: height,
            contentLength: input.ContentLength);

        return Task.FromResult(
            AiMediaProcessingResult.Success(
                input,
                processedContent: metadata,
                message:
                    "Image structural metadata inspected."));
    }

    /// <summary>
    /// Detects a supported image format
    /// and extracts its dimensions.
    /// </summary>
    private static bool TryInspectImage(
        ReadOnlySpan<byte> content,
        out string format,
        out string mimeType,
        out int width,
        out int height,
        out string error)
    {
        format = string.Empty;
        mimeType = string.Empty;
        width = 0;
        height = 0;
        error = string.Empty;

        if (IsPng(content))
        {
            format = "PNG";
            mimeType = "image/png";

            return TryReadPngDimensions(
                content,
                out width,
                out height,
                out error);
        }

        if (IsJpeg(content))
        {
            format = "JPEG";
            mimeType = "image/jpeg";

            return TryReadJpegDimensions(
                content,
                out width,
                out height,
                out error);
        }

        if (IsGif(content))
        {
            format = "GIF";
            mimeType = "image/gif";

            return TryReadGifDimensions(
                content,
                out width,
                out height,
                out error);
        }

        error =
            "Unsupported or unrecognized image format.";

        return false;
    }

    /// <summary>
    /// Detects the PNG file signature.
    /// </summary>
    private static bool IsPng(
        ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> signature =
        [
            137, 80, 78, 71, 13, 10, 26, 10
        ];

        return content.StartsWith(signature);
    }

    /// <summary>
    /// Reads PNG IHDR dimensions.
    /// </summary>
    private static bool TryReadPngDimensions(
        ReadOnlySpan<byte> content,
        out int width,
        out int height,
        out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        if (content.Length < 24)
        {
            error = "PNG header is incomplete.";

            return false;
        }

        var ihdrLength =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Slice(8, 4));

        if (ihdrLength != 13)
        {
            error = "PNG IHDR length is invalid.";

            return false;
        }

        ReadOnlySpan<byte> ihdr =
        [
            73, 72, 68, 82
        ];

        if (!content.Slice(12, 4).SequenceEqual(ihdr))
        {
            error = "PNG IHDR chunk is missing.";

            return false;
        }

        var rawWidth =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Slice(16, 4));

        var rawHeight =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Slice(20, 4));

        if (rawWidth == 0 ||
            rawHeight == 0 ||
            rawWidth > int.MaxValue ||
            rawHeight > int.MaxValue)
        {
            error = "PNG dimensions are invalid.";

            return false;
        }

        width = (int)rawWidth;

        height = (int)rawHeight;

        return true;
    }

    /// <summary>
    /// Detects JPEG start-of-image bytes.
    /// </summary>
    private static bool IsJpeg(
        ReadOnlySpan<byte> content)
    {
        return content.Length >= 4 &&
               content[0] == 0xFF &&
               content[1] == 0xD8;
    }

    /// <summary>
    /// Reads dimensions from JPEG
    /// start-of-frame marker segments.
    /// </summary>
    private static bool TryReadJpegDimensions(
        ReadOnlySpan<byte> content,
        out int width,
        out int height,
        out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        var offset = 2;

        while (offset < content.Length)
        {
            if (content[offset] != 0xFF)
            {
                error = "JPEG marker alignment is invalid.";

                return false;
            }

            while (offset < content.Length &&
                   content[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= content.Length)
            {
                break;
            }

            var marker = content[offset++];

            if (marker == 0xD9 || marker == 0xDA)
            {
                break;
            }

            if (marker == 0x01 ||
                (marker >= 0xD0 && marker <= 0xD7))
            {
                continue;
            }

            if (offset > content.Length - 2)
            {
                break;
            }

            var segmentLength =
                BinaryPrimitives.ReadUInt16BigEndian(
                    content.Slice(offset, 2));

            if (segmentLength < 2 ||
                segmentLength > content.Length - offset)
            {
                error = "JPEG segment length is invalid.";

                return false;
            }

            if (IsStartOfFrame(marker))
            {
                if (segmentLength < 7)
                {
                    error =
                        "JPEG frame header is incomplete.";

                    return false;
                }

                height =
                    BinaryPrimitives.ReadUInt16BigEndian(
                        content.Slice(offset + 3, 2));

                width =
                    BinaryPrimitives.ReadUInt16BigEndian(
                        content.Slice(offset + 5, 2));

                if (width == 0 || height == 0)
                {
                    error = "JPEG dimensions are invalid.";

                    return false;
                }

                return true;
            }

            offset += segmentLength;
        }

        error =
            "JPEG frame dimensions could not be found.";

        return false;
    }

    /// <summary>
    /// Determines whether a JPEG marker
    /// identifies a start-of-frame segment.
    /// </summary>
    private static bool IsStartOfFrame(
        byte marker)
    {
        return marker is
            0xC0 or
            0xC1 or
            0xC2 or
            0xC3 or
            0xC5 or
            0xC6 or
            0xC7 or
            0xC9 or
            0xCA or
            0xCB or
            0xCD or
            0xCE or
            0xCF;
    }

    /// <summary>
    /// Detects GIF87a and GIF89a signatures.
    /// </summary>
    private static bool IsGif(
        ReadOnlySpan<byte> content)
    {
        if (content.Length < 6)
        {
            return false;
        }

        ReadOnlySpan<byte> gif87a =
        [
            71, 73, 70, 56, 55, 97
        ];

        ReadOnlySpan<byte> gif89a =
        [
            71, 73, 70, 56, 57, 97
        ];

        return content.StartsWith(gif87a) ||
               content.StartsWith(gif89a);
    }

    /// <summary>
    /// Reads GIF logical screen dimensions.
    /// </summary>
    private static bool TryReadGifDimensions(
        ReadOnlySpan<byte> content,
        out int width,
        out int height,
        out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;

        if (content.Length < 10)
        {
            error = "GIF header is incomplete.";

            return false;
        }

        width =
            BinaryPrimitives.ReadUInt16LittleEndian(
                content.Slice(6, 2));

        height =
            BinaryPrimitives.ReadUInt16LittleEndian(
                content.Slice(8, 2));

        if (width == 0 || height == 0)
        {
            error = "GIF dimensions are invalid.";

            return false;
        }

        return true;
    }
}