namespace EnterpriseDocumentActionAgent.Domain.Documents;

/// <summary>
/// Represents the lifecycle state of an enterprise
/// knowledge document.
/// </summary>
public enum KnowledgeDocumentStatus
{
    Draft = 1,
    Published = 2,
    Archived = 3,
    Deleted = 4
}

/// <summary>
/// Represents a domain-level document validation failure.
/// </summary>
public sealed class KnowledgeDocumentDomainException : Exception
{
    public KnowledgeDocumentDomainException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Represents an enterprise knowledge document.
/// </summary>
/// <remarks>
/// This aggregate manages document metadata, version
/// references, and lifecycle transitions.
///
/// Document content is stored separately and must
/// be accessed through an authorized content store.
///
/// Tenant isolation and document authorization must
/// also be enforced by application and persistence layers.
/// </remarks>
public sealed class KnowledgeDocument
{
    private const int MaximumTenantIdLength = 200;
    private const int MaximumOwnerIdLength = 200;
    private const int MaximumTitleLength = 300;
    private const int MaximumContentTypeLength = 150;
    private const int MaximumVersionLength = 100;

    /// <summary>
    /// Required by persistence frameworks.
    /// </summary>
    private KnowledgeDocument()
    {
        TenantId = string.Empty;
        OwnerSubjectId = string.Empty;
        Title = string.Empty;
        ContentType = string.Empty;
        CurrentVersion = string.Empty;
    }

    private KnowledgeDocument(
        Guid id,
        string tenantId,
        string ownerSubjectId,
        string title,
        string contentType,
        string initialVersion,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        TenantId = tenantId;
        OwnerSubjectId = ownerSubjectId;
        Title = title;
        ContentType = contentType;
        CurrentVersion = initialVersion;
        Status = KnowledgeDocumentStatus.Draft;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string TenantId { get; private set; }

    public string OwnerSubjectId { get; private set; }

    public string Title { get; private set; }

    public string ContentType { get; private set; }

    public string CurrentVersion { get; private set; }

    public KnowledgeDocumentStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public bool IsDeleted =>
        Status == KnowledgeDocumentStatus.Deleted;

    public bool IsPublished =>
        Status == KnowledgeDocumentStatus.Published;

    /// <summary>
    /// Creates a new draft knowledge document.
    /// </summary>
    public static KnowledgeDocument Create(
        Guid id,
        string tenantId,
        string ownerSubjectId,
        string title,
        string contentType,
        string initialVersion,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new KnowledgeDocumentDomainException(
                "Document ID must not be empty.");
        }

        ValidateRequiredString(
            tenantId,
            MaximumTenantIdLength,
            nameof(tenantId));

        ValidateRequiredString(
            ownerSubjectId,
            MaximumOwnerIdLength,
            nameof(ownerSubjectId));

        ValidateRequiredString(
            title,
            MaximumTitleLength,
            nameof(title));

        ValidateRequiredString(
            contentType,
            MaximumContentTypeLength,
            nameof(contentType));

        ValidateRequiredString(
            initialVersion,
            MaximumVersionLength,
            nameof(initialVersion));

        ValidateTimestamp(createdAtUtc);

        return new KnowledgeDocument(
            id,
            tenantId.Trim(),
            ownerSubjectId.Trim(),
            title.Trim(),
            contentType.Trim(),
            initialVersion.Trim(),
            createdAtUtc);
    }

    /// <summary>
    /// Updates document metadata without modifying
    /// the underlying document content.
    /// </summary>
    public void UpdateMetadata(
        string title,
        string contentType,
        DateTimeOffset updatedAtUtc)
    {
        EnsureNotDeleted();

        ValidateRequiredString(
            title,
            MaximumTitleLength,
            nameof(title));

        ValidateRequiredString(
            contentType,
            MaximumContentTypeLength,
            nameof(contentType));

        ValidateTransitionTimestamp(updatedAtUtc);

        Title = title.Trim();
        ContentType = contentType.Trim();
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary>
    /// Advances the current document version reference.
    /// </summary>
    /// <remarks>
    /// The application layer must ensure that the
    /// referenced version exists and belongs to this
    /// document and tenant.
    /// </remarks>
    public void SetCurrentVersion(
        string version,
        DateTimeOffset updatedAtUtc)
    {
        EnsureNotDeleted();

        ValidateRequiredString(
            version,
            MaximumVersionLength,
            nameof(version));

        ValidateTransitionTimestamp(updatedAtUtc);

        if (string.Equals(
                CurrentVersion,
                version.Trim(),
                StringComparison.Ordinal))
        {
            throw new KnowledgeDocumentDomainException(
                "New document version must differ from the current version.");
        }

        CurrentVersion = version.Trim();
        UpdatedAtUtc = updatedAtUtc;
    }

    /// <summary>
    /// Publishes a draft or archived document.
    /// </summary>
    public void Publish(DateTimeOffset publishedAtUtc)
    {
        EnsureNotDeleted();

        if (Status == KnowledgeDocumentStatus.Published)
        {
            throw new KnowledgeDocumentDomainException(
                "Document is already published.");
        }

        ValidateTransitionTimestamp(publishedAtUtc);

        Status = KnowledgeDocumentStatus.Published;
        PublishedAtUtc = publishedAtUtc;
        ArchivedAtUtc = null;
        UpdatedAtUtc = publishedAtUtc;
    }

    /// <summary>
    /// Archives a published document.
    /// </summary>
    public void Archive(DateTimeOffset archivedAtUtc)
    {
        EnsureNotDeleted();

        if (Status != KnowledgeDocumentStatus.Published)
        {
            throw new KnowledgeDocumentDomainException(
                "Only published documents can be archived.");
        }

        ValidateTransitionTimestamp(archivedAtUtc);

        Status = KnowledgeDocumentStatus.Archived;
        ArchivedAtUtc = archivedAtUtc;
        UpdatedAtUtc = archivedAtUtc;
    }

    /// <summary>
    /// Marks a document as deleted.
    /// </summary>
    /// <remarks>
    /// This method changes domain state only.
    ///
    /// The application layer must enforce deletion
    /// authorization, approvals, and any required
    /// content cleanup or retention policies.
    /// </remarks>
    public void MarkDeleted(DateTimeOffset deletedAtUtc)
    {
        EnsureNotDeleted();

        ValidateTransitionTimestamp(deletedAtUtc);

        Status = KnowledgeDocumentStatus.Deleted;
        DeletedAtUtc = deletedAtUtc;
        UpdatedAtUtc = deletedAtUtc;
    }

    /// <summary>
    /// Validates that the document belongs to the
    /// expected tenant.
    /// </summary>
    public void EnsureTenant(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            !string.Equals(
                TenantId,
                tenantId,
                StringComparison.Ordinal))
        {
            throw new KnowledgeDocumentDomainException(
                "Document does not belong to the expected tenant.");
        }
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new KnowledgeDocumentDomainException(
                "Deleted documents cannot be modified.");
        }
    }

    private void ValidateTransitionTimestamp(
        DateTimeOffset timestamp)
    {
        ValidateTimestamp(timestamp);

        if (timestamp < UpdatedAtUtc)
        {
            throw new KnowledgeDocumentDomainException(
                "Document transition timestamp cannot move backward.");
        }
    }

    private static void ValidateTimestamp(
        DateTimeOffset timestamp)
    {
        if (timestamp == default ||
            timestamp.Offset != TimeSpan.Zero)
        {
            throw new KnowledgeDocumentDomainException(
                "Document timestamps must be valid UTC values.");
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