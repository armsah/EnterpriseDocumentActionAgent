using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Configures document content validation limits.
/// </summary>
public sealed record DocumentContentValidationOptions
{
    /// <summary>
    /// Maximum accepted document size in bytes.
    /// </summary>
    public long MaximumContentBytes { get; init; } =
        25L * 1024 * 1024;

    /// <summary>
    /// Indicates whether empty documents are allowed.
    /// </summary>
    public bool AllowEmptyContent { get; init; } = false;
}

/// <summary>
/// Represents a successful document content validation.
/// </summary>
public sealed record ValidatedDocumentContent(
    string ContentType,
    long ContentLength,
    string Sha256Hash);

/// <summary>
/// Represents a document content validation failure.
/// </summary>
public sealed class DocumentContentValidationException
    : Exception
{
    public DocumentContentValidationException(
        string message)
        : base(message)
    {
    }
}

/// <summary>
/// Validates document content before storage or indexing.
/// </summary>
/// <remarks>
/// This validator supports UTF-8 text, Markdown,
/// and JSON documents.
///
/// It validates content structure and integrity,
/// but does not perform malware scanning or
/// content sanitization.
///
/// Callers remain responsible for authorization,
/// tenant isolation, and safe downstream rendering.
/// </remarks>
public sealed class DocumentContentValidator
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    private readonly DocumentContentValidationOptions _options;

    public DocumentContentValidator(
        DocumentContentValidationOptions? options = null)
    {
        _options = options
            ?? new DocumentContentValidationOptions();

        if (_options.MaximumContentBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaximumContentBytes must be greater than zero.");
        }
    }

    /// <summary>
    /// Validates document content and computes its SHA-256 hash.
    /// </summary>
    /// <remarks>
    /// The supplied stream is read from its current position.
    ///
    /// The stream is not disposed by this method.
    ///
    /// The method buffers the document in memory and is
    /// intended for bounded document sizes.
    ///
    /// The caller must provide a fresh or rewound stream
    /// if the content will be read again afterward.
    /// </remarks>
    public async Task<ValidatedDocumentContent> ValidateAsync(
        Stream content,
        string contentType,
        long? declaredLength = null,
        string? expectedSha256Hash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        cancellationToken.ThrowIfCancellationRequested();

        if (!content.CanRead)
        {
            throw new DocumentContentValidationException(
                "Document content stream must be readable.");
        }

        var normalizedContentType =
            NormalizeContentType(contentType);

        using var buffer = new MemoryStream();

        var chunk = new byte[81920];

        long totalBytes = 0;

        while (true)
        {
            var bytesRead = await content.ReadAsync(
                chunk.AsMemory(),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            totalBytes = checked(totalBytes + bytesRead);

            if (totalBytes > _options.MaximumContentBytes)
            {
                throw new DocumentContentValidationException(
                    "Document content exceeds the maximum " +
                    "allowed size.");
            }

            await buffer.WriteAsync(
                chunk.AsMemory(0, bytesRead),
                cancellationToken);
        }

        if (totalBytes == 0 && !_options.AllowEmptyContent)
        {
            throw new DocumentContentValidationException(
                "Document content cannot be empty.");
        }

        if (declaredLength.HasValue &&
            declaredLength.Value != totalBytes)
        {
            throw new DocumentContentValidationException(
                "Declared document length does not match " +
                "the actual content length.");
        }

        var bytes = buffer.ToArray();

        ValidateTextContent(
            bytes,
            normalizedContentType);

        var computedHash = Convert.ToHexString(
            SHA256.HashData(bytes));

        if (!string.IsNullOrWhiteSpace(expectedSha256Hash))
        {
            if (!IsValidSha256Hash(expectedSha256Hash))
            {
                throw new DocumentContentValidationException(
                    "Expected SHA-256 hash must contain " +
                    "64 hexadecimal characters.");
            }

            var expectedHashBytes =
                Convert.FromHexString(expectedSha256Hash);

            var computedHashBytes =
                Convert.FromHexString(computedHash);

            if (!CryptographicOperations.FixedTimeEquals(
                expectedHashBytes,
                computedHashBytes))
            {
                throw new DocumentContentValidationException(
                    "Document content SHA-256 hash mismatch.");
            }
        }

        return new ValidatedDocumentContent(
            ContentType: normalizedContentType,
            ContentLength: totalBytes,
            Sha256Hash: computedHash);
    }

    /// <summary>
    /// Normalizes and validates the document MIME type.
    /// </summary>
    public static string NormalizeContentType(
        string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new DocumentContentValidationException(
                "Document content type is required.");
        }

        var normalized = contentType
            .Trim()
            .ToLowerInvariant();

        return normalized switch
        {
            "text/plain" => normalized,
            "text/markdown" => normalized,
            "application/json" => normalized,

            _ => throw new DocumentContentValidationException(
                "Unsupported document content type.")
        };
    }

    /// <summary>
    /// Validates UTF-8 content and JSON syntax when applicable.
    /// </summary>
    private static void ValidateTextContent(
        byte[] content,
        string contentType)
    {
        string text;

        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException)
        {
            throw new DocumentContentValidationException(
                "Document content is not valid UTF-8.");
        }

        if (contentType == "application/json")
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind ==
                    JsonValueKind.Undefined)
                {
                    throw new DocumentContentValidationException(
                        "JSON document content is invalid.");
                }
            }
            catch (JsonException)
            {
                throw new DocumentContentValidationException(
                    "Document content is not valid JSON.");
            }
        }
    }

    /// <summary>
    /// Checks whether a string is a valid SHA-256 hex digest.
    /// </summary>
    private static bool IsValidSha256Hash(
        string hash)
    {
        if (hash.Length != 64)
        {
            return false;
        }

        foreach (var character in hash)
        {
            var isHexadecimal =
                character is >= '0' and <= '9' or
                    >= 'a' and <= 'f' or
                    >= 'A' and <= 'F';

            if (!isHexadecimal)
            {
                return false;
            }
        }

        return true;
    }
}