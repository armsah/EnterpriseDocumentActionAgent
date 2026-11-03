namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Represents an optimistic-concurrency conflict
/// while creating, updating, or deleting document content.
/// </summary>
/// <remarks>
/// Content storage implementations should throw this
/// exception when the caller's expected content version
/// does not match the version currently stored.
///
/// ExpectedVersion may be null when the caller requested
/// creation of content that must not already exist.
///
/// ActualVersion may be null when no content currently
/// exists or when the storage implementation cannot
/// safely determine the current version.
///
/// This exception does not expose document content.
/// </remarks>
public sealed class DocumentContentVersionConflictException
    : Exception
{
    /// <summary>
    /// Gets the identifier of the affected document.
    /// </summary>
    public Guid DocumentId { get; }

    /// <summary>
    /// Gets the tenant containing the document.
    /// </summary>
    public string TenantId { get; }

    /// <summary>
    /// Gets the content version expected by the caller.
    /// </summary>
    public string? ExpectedVersion { get; }

    /// <summary>
    /// Gets the current content version, when known.
    /// </summary>
    public string? ActualVersion { get; }

    /// <summary>
    /// Creates a document content version conflict exception.
    /// </summary>
    public DocumentContentVersionConflictException(
        Guid documentId,
        string tenantId,
        string? expectedVersion,
        string? actualVersion)
        : this(
            documentId,
            tenantId,
            expectedVersion,
            actualVersion,
            innerException: null)
    {
    }

    /// <summary>
    /// Creates a document content version conflict exception
    /// with an underlying exception.
    /// </summary>
    public DocumentContentVersionConflictException(
        Guid documentId,
        string tenantId,
        string? expectedVersion,
        string? actualVersion,
        Exception? innerException)
        : base(
            CreateMessage(
                documentId,
                tenantId,
                expectedVersion,
                actualVersion),
            innerException)
    {
        ValidateDocumentId(documentId);

        TenantId = ValidateTenantId(tenantId);

        ExpectedVersion = ValidateVersion(
            expectedVersion,
            nameof(expectedVersion));

        ActualVersion = ValidateVersion(
            actualVersion,
            nameof(actualVersion));

        DocumentId = documentId;
    }

    /// <summary>
    /// Creates a safe diagnostic message without
    /// including document content or version tokens.
    /// </summary>
    private static string CreateMessage(
        Guid documentId,
        string tenantId,
        string? expectedVersion,
        string? actualVersion)
    {
        ValidateDocumentId(documentId);
        ValidateTenantId(tenantId);

        ValidateVersion(
            expectedVersion,
            nameof(expectedVersion));

        ValidateVersion(
            actualVersion,
            nameof(actualVersion));

        return
            "Document content version conflict. " +
            "The stored content version does not match " +
            "the version expected by the operation.";
    }

    private static void ValidateDocumentId(
        Guid documentId)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(documentId));
        }
    }

    private static string ValidateTenantId(
        string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            tenantId);

        var normalizedTenantId = tenantId.Trim();

        if (normalizedTenantId.Length > 128)
        {
            throw new ArgumentException(
                "Tenant identifier cannot exceed 128 characters.",
                nameof(tenantId));
        }

        return normalizedTenantId;
    }

    private static string? ValidateVersion(
        string? version,
        string parameterName)
    {
        if (version is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException(
                "Content version cannot be empty.",
                parameterName);
        }

        if (version.Length > 256)
        {
            throw new ArgumentException(
                "Content version cannot exceed 256 characters.",
                parameterName);
        }

        if (!string.Equals(
                version,
                version.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Content version must be normalized.",
                parameterName);
        }

        return version;
    }
}