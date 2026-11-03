namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Represents a request to update document metadata.
/// </summary>
/// <remarks>
/// Title, FileName, and Description are replacement values.
///
/// ExpectedRevision must match the current metadata
/// revision to prevent lost updates.
///
/// An empty Description clears the description.
/// </remarks>
public sealed record UpdateDocumentRequest(
    Guid DocumentId,
    string TenantId,
    string Title,
    string FileName,
    string? Description,
    string ExpectedRevision);

/// <summary>
/// Represents versioned document metadata.
/// </summary>
public sealed record VersionedDocumentMetadata(
    DocumentMetadata Metadata,
    string Revision,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Defines storage operations for versioned metadata.
/// </summary>
/// <remarks>
/// Implementations must enforce tenant-scoped reads
/// and atomic revision checks on updates.
///
/// A revision conflict must be reported rather than
/// silently overwriting newer metadata.
/// </remarks>
public interface IDocumentMetadataWriter
{
    /// <summary>
    /// Retrieves versioned metadata for a document.
    /// </summary>
    Task<VersionedDocumentMetadata?> GetVersionedAsync(
        Guid documentId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically replaces mutable document metadata
    /// when the expected revision matches.
    /// </summary>
    Task<VersionedDocumentMetadata> UpdateAsync(
        DocumentMetadata metadata,
        string expectedRevision,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the application operation for updating
/// document metadata.
/// </summary>
public interface IUpdateDocument
{
    /// <summary>
    /// Updates mutable metadata and returns its new revision.
    /// </summary>
    Task<VersionedDocumentMetadata> ExecuteAsync(
        UpdateDocumentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates validated, tenant-scoped document
/// metadata updates.
/// </summary>
/// <remarks>
/// This operation does not authenticate callers,
/// authorize document modifications, or verify
/// human approval decisions.
///
/// The hosting application must enforce those
/// controls before invoking this operation.
///
/// The metadata writer is responsible for atomic
/// optimistic-concurrency enforcement.
/// </remarks>
public sealed class UpdateDocument : IUpdateDocument
{
    private readonly IDocumentMetadataWriter _metadataWriter;

    /// <summary>
    /// Creates a document metadata update operation.
    /// </summary>
    public UpdateDocument(
        IDocumentMetadataWriter metadataWriter)
    {
        _metadataWriter = metadataWriter
            ?? throw new ArgumentNullException(
                nameof(metadataWriter));
    }

    /// <inheritdoc />
    public async Task<VersionedDocumentMetadata> ExecuteAsync(
        UpdateDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequest(request);

        var tenantId = request.TenantId.Trim();

        var existing = await _metadataWriter.GetVersionedAsync(
            request.DocumentId,
            tenantId,
            cancellationToken);

        if (existing is null)
        {
            throw new KeyNotFoundException(
                "The document was not found.");
        }

        ValidateVersionedMetadata(
            existing,
            request.DocumentId,
            tenantId);

        // The request changes only mutable fields.
        // Document identity, tenant, creator, and
        // creation timestamp remain unchanged.
        var updatedMetadata = existing.Metadata with
        {
            Title = request.Title.Trim(),
            FileName = request.FileName.Trim(),
            Description = NormalizeOptionalText(
                request.Description)
        };

        // The writer must compare the expected revision
        // atomically with the current stored revision.
        var updated = await _metadataWriter.UpdateAsync(
            updatedMetadata,
            request.ExpectedRevision,
            cancellationToken);

        ValidateVersionedMetadata(
            updated,
            request.DocumentId,
            tenantId);

        // Reject a writer that returns different
        // mutable metadata from what was requested.
        if (updated.Metadata.Title != updatedMetadata.Title ||
            updated.Metadata.FileName != updatedMetadata.FileName ||
            updated.Metadata.Description !=
                updatedMetadata.Description)
        {
            throw new InvalidOperationException(
                "The metadata writer returned unexpected " +
                "document metadata.");
        }

        // Immutable fields must remain unchanged.
        if (updated.Metadata.CreatedBy !=
                existing.Metadata.CreatedBy ||
            updated.Metadata.CreatedAtUtc !=
                existing.Metadata.CreatedAtUtc)
        {
            throw new InvalidOperationException(
                "The metadata writer changed immutable " +
                "document properties.");
        }

        return updated;
    }

    private static void ValidateRequest(
        UpdateDocumentRequest request)
    {
        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.TenantId);

        if (request.TenantId.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Tenant identifier cannot exceed 128 characters.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Title);

        if (request.Title.Trim().Length > 256)
        {
            throw new ArgumentException(
                "Document title cannot exceed 256 characters.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.FileName);

        var fileName = request.FileName.Trim();

        if (fileName.Length > 255)
        {
            throw new ArgumentException(
                "File name cannot exceed 255 characters.",
                nameof(request));
        }

        if (fileName is "." or ".." ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "File name contains invalid characters.",
                nameof(request));
        }

        if (request.Description is not null &&
            request.Description.Trim().Length > 2000)
        {
            throw new ArgumentException(
                "Document description cannot exceed " +
                "2000 characters.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.ExpectedRevision);

        if (request.ExpectedRevision.Trim().Length > 256)
        {
            throw new ArgumentException(
                "Expected revision cannot exceed " +
                "256 characters.",
                nameof(request));
        }

        if (!string.Equals(
                request.ExpectedRevision,
                request.ExpectedRevision.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Expected revision must be normalized.",
                nameof(request));
        }
    }

    private static void ValidateVersionedMetadata(
        VersionedDocumentMetadata versioned,
        Guid expectedDocumentId,
        string expectedTenantId)
    {
        ArgumentNullException.ThrowIfNull(versioned);

        if (versioned.Metadata is null)
        {
            throw new InvalidOperationException(
                "The metadata writer returned null metadata.");
        }

        if (versioned.Metadata.DocumentId != expectedDocumentId)
        {
            throw new InvalidOperationException(
                "The metadata writer returned an unexpected " +
                "document identifier.");
        }

        if (!string.Equals(
                versioned.Metadata.TenantId,
                expectedTenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The metadata writer returned metadata " +
                "outside the requested tenant scope.");
        }

        if (string.IsNullOrWhiteSpace(
                versioned.Revision))
        {
            throw new InvalidOperationException(
                "The metadata writer returned an invalid " +
                "metadata revision.");
        }

        if (versioned.UpdatedAtUtc == default)
        {
            throw new InvalidOperationException(
                "The metadata writer returned an invalid " +
                "update timestamp.");
        }
    }

    private static string? NormalizeOptionalText(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}