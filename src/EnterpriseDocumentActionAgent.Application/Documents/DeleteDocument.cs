namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Represents a request to delete a document.
/// </summary>
/// <remarks>
/// ExpectedRevision must match the current metadata
/// revision to prevent deletion of a document that
/// has changed since the caller last reviewed it.
///
/// RequestedBy must come from an authenticated identity.
/// It is not proof of authorization or approval.
/// </remarks>
public sealed record DeleteDocumentRequest(
    Guid DocumentId,
    string TenantId,
    string ExpectedRevision,
    string RequestedBy);

/// <summary>
/// Represents the outcome of a document deletion.
/// </summary>
public sealed record DeleteDocumentResult(
    Guid DocumentId,
    string TenantId,
    bool Deleted,
    DateTimeOffset? DeletedAtUtc);

/// <summary>
/// Defines the persistence boundary for document deletion.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Enforce tenant-scoped document access.
/// - Atomically compare the expected metadata revision.
/// - Prevent concurrent updates from bypassing deletion.
/// - Coordinate metadata and content cleanup.
/// - Preserve audit and compliance requirements.
/// - Arrange reliable search-index cleanup.
///
/// Deletion may require a durable workflow because
/// metadata, binary content, and search indexes can
/// reside in different storage systems.
///
/// A revision conflict must not be reported as
/// a successful deletion.
///
/// This interface does not authenticate callers
/// or authorize deletion.
/// </remarks>
public interface IDocumentDeletionStore
{
    /// <summary>
    /// Deletes a document when its revision matches.
    /// </summary>
    /// <returns>
    /// True when the document was deleted.
    /// False when the document no longer exists.
    /// </returns>
    Task<bool> DeleteAsync(
        Guid documentId,
        string tenantId,
        string expectedRevision,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the application operation for deleting documents.
/// </summary>
public interface IDeleteDocument
{
    /// <summary>
    /// Deletes a document within a tenant.
    /// </summary>
    Task<DeleteDocumentResult> ExecuteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates validated, tenant-scoped document deletion.
/// </summary>
/// <remarks>
/// Document deletion is a high-risk operation.
///
/// The hosting application must authenticate the
/// requester, enforce document-level ACLs, and
/// verify any required human approval before
/// invoking this operation.
///
/// RequestedBy is retained in the request for
/// future audit integration, but is not currently
/// persisted by this operation.
///
/// The deletion store must enforce atomic revision
/// checks and coordinate durable cleanup.
///
/// Search-index cleanup should be scheduled through
/// a reliable deletion event or transactional outbox.
/// </remarks>
public sealed class DeleteDocument : IDeleteDocument
{
    private readonly IDocumentMetadataWriter _metadataWriter;
    private readonly IDocumentDeletionStore _deletionStore;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a document deletion operation.
    /// </summary>
    public DeleteDocument(
        IDocumentMetadataWriter metadataWriter,
        IDocumentDeletionStore deletionStore,
        TimeProvider? timeProvider = null)
    {
        _metadataWriter = metadataWriter
            ?? throw new ArgumentNullException(
                nameof(metadataWriter));

        _deletionStore = deletionStore
            ?? throw new ArgumentNullException(
                nameof(deletionStore));

        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<DeleteDocumentResult> ExecuteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequest(request);

        var tenantId = request.TenantId.Trim();

        // Read the current metadata for defensive
        // tenant and document scope validation.
        var existing = await _metadataWriter.GetVersionedAsync(
            request.DocumentId,
            tenantId,
            cancellationToken);

        if (existing is null)
        {
            return new DeleteDocumentResult(
                DocumentId: request.DocumentId,
                TenantId: tenantId,
                Deleted: false,
                DeletedAtUtc: null);
        }

        ValidateExistingMetadata(
            existing,
            request.DocumentId,
            tenantId);

        // This preliminary comparison provides a
        // clear conflict before attempting deletion.
        //
        // It is NOT sufficient for concurrency safety.
        // The deletion store must compare the revision
        // atomically when performing the deletion.
        if (!string.Equals(
                existing.Revision,
                request.ExpectedRevision,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Document metadata revision conflict. " +
                "The document has changed since it " +
                "was last reviewed.");
        }

        // The storage implementation must enforce
        // tenant isolation and revision matching
        // as part of the deletion operation.
        var deleted = await _deletionStore.DeleteAsync(
            request.DocumentId,
            tenantId,
            request.ExpectedRevision,
            cancellationToken);

        if (!deleted)
        {
            return new DeleteDocumentResult(
                DocumentId: request.DocumentId,
                TenantId: tenantId,
                Deleted: false,
                DeletedAtUtc: null);
        }

        return new DeleteDocumentResult(
            DocumentId: request.DocumentId,
            TenantId: tenantId,
            Deleted: true,
            DeletedAtUtc: _timeProvider.GetUtcNow());
    }

    private static void ValidateRequest(
        DeleteDocumentRequest request)
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
            request.ExpectedRevision);

        if (request.ExpectedRevision.Length > 256)
        {
            throw new ArgumentException(
                "Expected revision cannot exceed 256 characters.",
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

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.RequestedBy);

        if (request.RequestedBy.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Requester identifier cannot exceed 128 characters.",
                nameof(request));
        }
    }

    private static void ValidateExistingMetadata(
        VersionedDocumentMetadata existing,
        Guid expectedDocumentId,
        string expectedTenantId)
    {
        ArgumentNullException.ThrowIfNull(existing);

        if (existing.Metadata is null)
        {
            throw new InvalidOperationException(
                "The metadata reader returned null metadata.");
        }

        if (existing.Metadata.DocumentId != expectedDocumentId)
        {
            throw new InvalidOperationException(
                "The metadata reader returned an unexpected " +
                "document identifier.");
        }

        if (!string.Equals(
                existing.Metadata.TenantId,
                expectedTenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The metadata reader returned metadata " +
                "outside the requested tenant scope.");
        }

        if (string.IsNullOrWhiteSpace(existing.Revision))
        {
            throw new InvalidOperationException(
                "The metadata reader returned an invalid " +
                "metadata revision.");
        }
    }
}