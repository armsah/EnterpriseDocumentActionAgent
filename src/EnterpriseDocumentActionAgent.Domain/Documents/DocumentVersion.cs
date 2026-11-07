using System.Text.RegularExpressions;

namespace EnterpriseDocumentActionAgent.Domain.Documents;

/// <summary>
/// Identifies the hashing algorithm used to calculate
/// the content integrity digest of a document version.
/// </summary>
public enum DocumentContentHashAlgorithm
{
    Sha256 = 1
}

/// <summary>
/// Represents immutable metadata for a specific
/// version of a knowledge document.
/// </summary>
/// <remarks>
/// Document content is stored separately.
///
/// The content hash is used to verify integrity when
/// retrieving content from the underlying content store.
///
/// This model does not enforce authorization or
/// tenant isolation by itself.
/// </remarks>
public sealed class DocumentVersion
{
    private const int MaximumTenantIdLength = 200;
    private const int MaximumVersionIdLength = 100;
    private const int MaximumContentTypeLength = 150;
    private const int MaximumContentReferenceLength = 1000;

    private static readonly Regex Sha256Pattern = new(
        "^[a-fA-F0-9]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Required by persistence frameworks.
    /// </summary>
    private DocumentVersion()
    {
        TenantId = string.Empty;
        VersionId = string.Empty;
        ContentType = string.Empty;
        ContentReference = string.Empty;
        ContentHash = string.Empty;
    }

    private DocumentVersion(
        Guid id,
        Guid documentId,
        string tenantId,
        string versionId,
        string contentType,
        string contentReference,
        long contentLength,
        string contentHash,
        DocumentContentHashAlgorithm hashAlgorithm,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        DocumentId = documentId;
        TenantId = tenantId;
        VersionId = versionId;
        ContentType = contentType;
        ContentReference = contentReference;
        ContentLength = contentLength;
        ContentHash = contentHash;
        HashAlgorithm = hashAlgorithm;
        CreatedAtUtc = createdAtUtc;
    }

    /// <summary>
    /// Unique identity of this document version.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Identity of the knowledge document
    /// associated with this version.
    /// </summary>
    public Guid DocumentId { get; private set; }

    /// <summary>
    /// Tenant that owns the document version.
    /// </summary>
    public string TenantId { get; private set; }

    /// <summary>
    /// Version identifier referenced by
    /// KnowledgeDocument.CurrentVersion.
    /// </summary>
    public string VersionId { get; private set; }

    /// <summary>
    /// MIME type of the document content.
    /// </summary>
    public string ContentType { get; private set; }

    /// <summary>
    /// Opaque reference to the stored document content.
    /// </summary>
    /// <remarks>
    /// This value must be treated as a storage identifier,
    /// not as a client-controlled filesystem path or URL.
    /// </remarks>
    public string ContentReference { get; private set; }

    /// <summary>
    /// Length of the content in bytes.
    /// </summary>
    public long ContentLength { get; private set; }

    /// <summary>
    /// Hexadecimal digest of the stored content.
    /// </summary>
    public string ContentHash { get; private set; }

    /// <summary>
    /// Algorithm used for the content digest.
    /// </summary>
    public DocumentContentHashAlgorithm HashAlgorithm
    {
        get;
        private set;
    }

    /// <summary>
    /// UTC timestamp when the version was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>
    /// Creates a new immutable document version.
    /// </summary>
    public static DocumentVersion Create(
        Guid id,
        Guid documentId,
        string tenantId,
        string versionId,
        string contentType,
        string contentReference,
        long contentLength,
        string contentHash,
        DocumentContentHashAlgorithm hashAlgorithm,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new KnowledgeDocumentDomainException(
                "Document version ID must not be empty.");
        }

        if (documentId == Guid.Empty)
        {
            throw new KnowledgeDocumentDomainException(
                "Document ID must not be empty.");
        }

        ValidateRequiredString(
            tenantId,
            MaximumTenantIdLength,
            nameof(tenantId));

        ValidateRequiredString(
            versionId,
            MaximumVersionIdLength,
            nameof(versionId));

        ValidateRequiredString(
            contentType,
            MaximumContentTypeLength,
            nameof(contentType));

        ValidateRequiredString(
            contentReference,
            MaximumContentReferenceLength,
            nameof(contentReference));

        if (contentLength < 0)
        {
            throw new KnowledgeDocumentDomainException(
                "Document content length cannot be negative.");
        }

        if (hashAlgorithm != DocumentContentHashAlgorithm.Sha256)
        {
            throw new KnowledgeDocumentDomainException(
                "Unsupported document content hash algorithm.");
        }

        if (string.IsNullOrWhiteSpace(contentHash) ||
            !Sha256Pattern.IsMatch(contentHash))
        {
            throw new KnowledgeDocumentDomainException(
                "Document content hash must be a valid SHA-256 digest.");
        }

        ValidateTimestamp(createdAtUtc);

        return new DocumentVersion(
            id,
            documentId,
            tenantId.Trim(),
            versionId.Trim(),
            contentType.Trim(),
            contentReference.Trim(),
            contentLength,
            contentHash.ToLowerInvariant(),
            hashAlgorithm,
            createdAtUtc);
    }

    /// <summary>
    /// Ensures that the version belongs to
    /// the expected document and tenant.
    /// </summary>
    public void EnsureOwnership(
        Guid documentId,
        string tenantId)
    {
        if (documentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(tenantId) ||
            DocumentId != documentId ||
            !string.Equals(
                TenantId,
                tenantId,
                StringComparison.Ordinal))
        {
            throw new KnowledgeDocumentDomainException(
                "Document version does not belong to the expected document and tenant.");
        }
    }

    /// <summary>
    /// Verifies whether a calculated content hash
    /// matches the stored version metadata.
    /// </summary>
    /// <remarks>
    /// The caller is responsible for calculating
    /// the digest from the actual content bytes.
    /// </remarks>
    public bool MatchesContentHash(string? calculatedHash)
    {
        return !string.IsNullOrWhiteSpace(calculatedHash) &&
            string.Equals(
                ContentHash,
                calculatedHash,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateTimestamp(
        DateTimeOffset timestamp)
    {
        if (timestamp == default ||
            timestamp.Offset != TimeSpan.Zero)
        {
            throw new KnowledgeDocumentDomainException(
                "Document version timestamp must be a valid UTC value.");
        }
    }

    private static void ValidateRequiredString(
        string? value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new KnowledgeDocumentDomainException(
                $"{fieldName} is required.");
        }

        if (value.Trim().Length > maximumLength)
        {
            throw new KnowledgeDocumentDomainException(
                $"{fieldName} exceeds the maximum allowed length.");
        }
    }
}