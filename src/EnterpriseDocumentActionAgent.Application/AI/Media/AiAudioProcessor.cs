using System.Buffers.Binary;

namespace EnterpriseDocumentActionAgent.Application.AI.Media;

/// <summary>
/// Represents structural metadata extracted
/// from a supported audio file.
/// </summary>
/// <remarks>
/// Metadata inspection does not imply
/// successful audio decoding or transcription.
/// </remarks>
public sealed record AiAudioMetadata
{
    /// <summary>
    /// Gets the media identifier.
    /// </summary>
    public string MediaId { get; }

    /// <summary>
    /// Gets the detected audio format.
    /// </summary>
    public string Format { get; }

    /// <summary>
    /// Gets the validated MIME type.
    /// </summary>
    public string MimeType { get; }

    /// <summary>
    /// Gets the audio sample rate.
    /// </summary>
    public int SampleRate { get; }

    /// <summary>
    /// Gets the number of audio channels.
    /// </summary>
    public int Channels { get; }

    /// <summary>
    /// Gets the nominal bits per sample,
    /// if available.
    /// </summary>
    public int? BitsPerSample { get; }

    /// <summary>
    /// Gets the estimated or declared duration,
    /// if available.
    /// </summary>
    public TimeSpan? Duration { get; }

    /// <summary>
    /// Gets whether the duration is derived
    /// from complete sample-count metadata.
    /// </summary>
    public bool HasDeclaredSampleCount { get; }

    /// <summary>
    /// Gets the input size in bytes.
    /// </summary>
    public int ContentLength { get; }

    /// <summary>
    /// Gets the inspection timestamp.
    /// </summary>
    public DateTimeOffset InspectedAtUtc { get; }

    /// <summary>
    /// Initializes audio metadata.
    /// </summary>
    public AiAudioMetadata(
        string mediaId,
        string format,
        string mimeType,
        int sampleRate,
        int channels,
        int? bitsPerSample,
        TimeSpan? duration,
        bool hasDeclaredSampleCount,
        int contentLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channels));
        }

        if (bitsPerSample is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitsPerSample));
        }

        if (duration is { } value &&
            value < TimeSpan.Zero)
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
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        Duration = duration;
        HasDeclaredSampleCount = hasDeclaredSampleCount;
        ContentLength = contentLength;
        InspectedAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Performs bounded structural inspection
/// of WAV, MP3, and FLAC audio inputs.
/// </summary>
/// <remarks>
/// WAV inspection supports RIFF/WAVE
/// with PCM or IEEE floating-point data.
///
/// MP3 inspection reads a valid MPEG
/// Layer III frame header. It does not
/// decode or scan every audio frame.
///
/// FLAC inspection reads STREAMINFO.
///
/// This processor does not perform
/// speech recognition, audio decoding,
/// or malware scanning.
/// </remarks>
public sealed class AiAudioProcessor : IAiMediaHandler
{
    public const int DefaultMaxAudioBytes =
        25 * 1024 * 1024;

    public const int DefaultMaxSampleRate = 192000;

    public const int DefaultMaxChannels = 8;

    public static readonly TimeSpan DefaultMaxDuration =
        TimeSpan.FromHours(2);

    /// <inheritdoc />
    public AiMediaKind Kind => AiMediaKind.Audio;

    public int MaxAudioBytes { get; }

    public int MaxSampleRate { get; }

    public int MaxChannels { get; }

    public TimeSpan MaxDuration { get; }

    /// <summary>
    /// Initializes an audio processor.
    /// </summary>
    public AiAudioProcessor(
        int maxAudioBytes = DefaultMaxAudioBytes,
        int maxSampleRate = DefaultMaxSampleRate,
        int maxChannels = DefaultMaxChannels,
        TimeSpan? maxDuration = null)
    {
        if (maxAudioBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxAudioBytes));
        }

        if (maxSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSampleRate));
        }

        if (maxChannels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxChannels));
        }

        var durationLimit =
            maxDuration ?? DefaultMaxDuration;

        if (durationLimit <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDuration));
        }

        MaxAudioBytes = maxAudioBytes;
        MaxSampleRate = maxSampleRate;
        MaxChannels = maxChannels;
        MaxDuration = durationLimit;
    }

    /// <summary>
    /// Inspects an audio input and returns
    /// structural metadata.
    /// </summary>
    public Task<AiMediaProcessingResult> ProcessAsync(
        AiMediaInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        cancellationToken.ThrowIfCancellationRequested();

        if (input.Kind != AiMediaKind.Audio)
        {
            return Failure(
                input,
                "The supplied media is not audio.");
        }

        if (input.ContentLength > MaxAudioBytes)
        {
            return Failure(
                input,
                $"Audio exceeds the configured limit " +
                $"of {MaxAudioBytes} bytes.");
        }

        var content = input.Content.Span;

        if (!TryInspect(
            content,
            out var format,
            out var mimeType,
            out var sampleRate,
            out var channels,
            out var bitsPerSample,
            out var duration,
            out var hasDeclaredSampleCount,
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

        if (sampleRate > MaxSampleRate)
        {
            return Failure(
                input,
                $"Sample rate {sampleRate} exceeds " +
                $"the configured limit of " +
                $"{MaxSampleRate} Hz.");
        }

        if (channels > MaxChannels)
        {
            return Failure(
                input,
                $"Channel count {channels} exceeds " +
                $"the configured limit of " +
                $"{MaxChannels}.");
        }

        if (duration is { } detectedDuration &&
            detectedDuration > MaxDuration)
        {
            return Failure(
                input,
                $"Audio duration exceeds the " +
                $"configured limit of " +
                $"{MaxDuration}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var metadata = new AiAudioMetadata(
            mediaId: input.MediaId,
            format: format,
            mimeType: mimeType,
            sampleRate: sampleRate,
            channels: channels,
            bitsPerSample: bitsPerSample,
            duration: duration,
            hasDeclaredSampleCount:
                hasDeclaredSampleCount,
            contentLength: input.ContentLength);

        return Task.FromResult(
            AiMediaProcessingResult.Success(
                input,
                processedContent: metadata,
                message:
                    "Audio structural metadata inspected."));
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
    /// Detects a supported audio format.
    /// </summary>
    private static bool TryInspect(
        ReadOnlySpan<byte> content,
        out string format,
        out string mimeType,
        out int sampleRate,
        out int channels,
        out int? bitsPerSample,
        out TimeSpan? duration,
        out bool hasDeclaredSampleCount,
        out string error)
    {
        format = string.Empty;
        mimeType = string.Empty;
        sampleRate = 0;
        channels = 0;
        bitsPerSample = null;
        duration = null;
        hasDeclaredSampleCount = false;
        error = string.Empty;

        if (IsWav(content))
        {
            format = "WAV";
            mimeType = "audio/wav";

            return TryReadWav(
                content,
                out sampleRate,
                out channels,
                out bitsPerSample,
                out duration,
                out error);
        }

        if (IsFlac(content))
        {
            format = "FLAC";
            mimeType = "audio/flac";

            return TryReadFlac(
                content,
                out sampleRate,
                out channels,
                out bitsPerSample,
                out duration,
                out hasDeclaredSampleCount,
                out error);
        }

        if (TryReadMp3(
            content,
            out sampleRate,
            out channels,
            out error))
        {
            format = "MP3";
            mimeType = "audio/mpeg";
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(error))
        {
            error =
                "Unsupported or unrecognized audio format.";
        }

        return false;
    }

    /// <summary>
    /// Detects RIFF/WAVE.
    /// </summary>
    private static bool IsWav(
        ReadOnlySpan<byte> content)
    {
        return content.Length >= 12 &&
               content.Slice(0, 4)
                   .SequenceEqual("RIFF"u8) &&
               content.Slice(8, 4)
                   .SequenceEqual("WAVE"u8);
    }

    /// <summary>
    /// Reads WAV format and data chunks.
    /// </summary>
    private static bool TryReadWav(
        ReadOnlySpan<byte> content,
        out int sampleRate,
        out int channels,
        out int? bitsPerSample,
        out TimeSpan? duration,
        out string error)
    {
        sampleRate = 0;
        channels = 0;
        bitsPerSample = null;
        duration = null;
        error = string.Empty;

        var riffSize =
            BinaryPrimitives.ReadUInt32LittleEndian(
                content.Slice(4, 4));

        if ((long)riffSize + 8 > content.Length)
        {
            error = "WAV RIFF length exceeds input size.";
            return false;
        }

        var limit = (int)((long)riffSize + 8);

        var offset = 12;
        var foundFormat = false;
        var foundData = false;
        uint byteRate = 0;
        ushort blockAlign = 0;
        uint dataLength = 0;

        while (offset <= limit - 8)
        {
            var chunkId = content.Slice(offset, 4);

            var chunkSize =
                BinaryPrimitives.ReadUInt32LittleEndian(
                    content.Slice(offset + 4, 4));

            var dataOffset = offset + 8;

            if (chunkSize > limit - dataOffset)
            {
                error = "WAV chunk exceeds RIFF boundary.";
                return false;
            }

            if (chunkId.SequenceEqual("fmt "u8))
            {
                if (chunkSize < 16)
                {
                    error = "WAV format chunk is incomplete.";
                    return false;
                }

                var formatCode =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        content.Slice(dataOffset, 2));

                if (formatCode is not (1 or 3))
                {
                    error =
                        "Unsupported WAV encoding.";
                    return false;
                }

                channels =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        content.Slice(dataOffset + 2, 2));

                sampleRate =
                    (int)BinaryPrimitives
                        .ReadUInt32LittleEndian(
                            content.Slice(dataOffset + 4, 4));

                byteRate =
                    BinaryPrimitives.ReadUInt32LittleEndian(
                        content.Slice(dataOffset + 8, 4));

                blockAlign =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        content.Slice(dataOffset + 12, 2));

                bitsPerSample =
                    BinaryPrimitives.ReadUInt16LittleEndian(
                        content.Slice(dataOffset + 14, 2));

                if (channels <= 0 ||
                    sampleRate <= 0 ||
                    byteRate == 0 ||
                    blockAlign == 0 ||
                    bitsPerSample <= 0)
                {
                    error = "WAV format values are invalid.";
                    return false;
                }

                foundFormat = true;
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                dataLength = chunkSize;
                foundData = true;
            }

            var paddedSize =
                (long)chunkSize + (chunkSize & 1);

            var nextOffset =
                (long)dataOffset + paddedSize;

            if (nextOffset > limit)
            {
                error = "WAV chunk padding is invalid.";
                return false;
            }

            offset = (int)nextOffset;
        }

        if (!foundFormat || !foundData)
        {
            error = "WAV format or data chunk is missing.";
            return false;
        }

        if (dataLength % blockAlign != 0)
        {
            error = "WAV data alignment is invalid.";
            return false;
        }

        var seconds = (double)dataLength / byteRate;

        duration = TimeSpan.FromSeconds(seconds);

        return true;
    }

    /// <summary>
    /// Detects the FLAC signature.
    /// </summary>
    private static bool IsFlac(
        ReadOnlySpan<byte> content)
    {
        return content.StartsWith("fLaC"u8);
    }

    /// <summary>
    /// Reads FLAC STREAMINFO metadata.
    /// </summary>
    private static bool TryReadFlac(
        ReadOnlySpan<byte> content,
        out int sampleRate,
        out int channels,
        out int? bitsPerSample,
        out TimeSpan? duration,
        out bool hasDeclaredSampleCount,
        out string error)
    {
        sampleRate = 0;
        channels = 0;
        bitsPerSample = null;
        duration = null;
        hasDeclaredSampleCount = false;
        error = string.Empty;

        if (content.Length < 42)
        {
            error = "FLAC STREAMINFO is incomplete.";
            return false;
        }

        var blockHeader = content[4];

        var blockType = blockHeader & 0x7F;

        var blockLength =
            (content[5] << 16) |
            (content[6] << 8) |
            content[7];

        if (blockType != 0 || blockLength != 34)
        {
            error = "FLAC STREAMINFO block is invalid.";
            return false;
        }

        ulong packed =
            BinaryPrimitives.ReadUInt64BigEndian(
                content.Slice(18, 8));

        sampleRate = (int)((packed >> 44) & 0xFFFFF);

        channels = (int)(((packed >> 41) & 0x7) + 1);

        bitsPerSample =
            (int)(((packed >> 36) & 0x1F) + 1);

        var totalSamples =
            packed & 0xFFFFFFFFF;

        if (sampleRate <= 0 ||
            channels <= 0 ||
            bitsPerSample <= 0)
        {
            error = "FLAC stream values are invalid.";
            return false;
        }

        if (totalSamples > 0)
        {
            duration = TimeSpan.FromSeconds(
                (double)totalSamples / sampleRate);

            hasDeclaredSampleCount = true;
        }

        return true;
    }

    /// <summary>
    /// Reads a bounded MPEG Layer III
    /// frame header, optionally after ID3v2.
    /// </summary>
    private static bool TryReadMp3(
        ReadOnlySpan<byte> content,
        out int sampleRate,
        out int channels,
        out string error)
    {
        sampleRate = 0;
        channels = 0;
        error = string.Empty;

        var offset = 0;

        if (content.Length >= 10 &&
            content.Slice(0, 3).SequenceEqual("ID3"u8))
        {
            var tagSize = 0;

            for (var i = 6; i < 10; i++)
            {
                if ((content[i] & 0x80) != 0)
                {
                    error = "Invalid ID3v2 tag size.";
                    return false;
                }

                tagSize = (tagSize << 7) | content[i];
            }

            offset = tagSize + 10;

            if (offset > content.Length - 4)
            {
                error = "MP3 frame header is missing.";
                return false;
            }
        }

        if (offset > content.Length - 4)
        {
            error = "MP3 frame header is incomplete.";
            return false;
        }

        uint header =
            BinaryPrimitives.ReadUInt32BigEndian(
                content.Slice(offset, 4));

        if ((header & 0xFFE00000) != 0xFFE00000)
        {
            error = "MP3 synchronization bits are invalid.";
            return false;
        }

        var versionBits = (int)((header >> 19) & 0x3);
        var layerBits = (int)((header >> 17) & 0x3);
        var bitrateIndex = (int)((header >> 12) & 0xF);
        var sampleRateIndex = (int)((header >> 10) & 0x3);
        var channelMode = (int)((header >> 6) & 0x3);

        if (versionBits == 1 ||
            layerBits != 1 ||
            bitrateIndex is 0 or 15 ||
            sampleRateIndex == 3)
        {
            error = "Unsupported MP3 frame header.";
            return false;
        }

        var baseRates = new[] { 44100, 48000, 32000 };

        var rate = baseRates[sampleRateIndex];

        sampleRate = versionBits switch
        {
            3 => rate,
            2 => rate / 2,
            0 => rate / 4,
            _ => 0
        };

        channels = channelMode == 3 ? 1 : 2;

        if (sampleRate <= 0)
        {
            error = "MP3 sample rate is invalid.";
            return false;
        }

        return true;
    }
}