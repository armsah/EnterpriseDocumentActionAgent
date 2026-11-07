using EnterpriseDocumentActionAgent.Domain.Documents;

namespace EnterpriseDocumentActionAgent.Infrastructure.Persistence;

/// <summary>
/// Represents a repository operation that violates
/// document persistence invariants.
/// </summary>
public sealed class KnowledgeDocumentRepositoryException : Exception
{
    public KnowledgeDocumentRepositoryException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Represents an optimistic concurrency conflict
/// while updating document metadata.
/// </summary>
public sealed class KnowledgeDocumentConcurrencyException : Exception
{
    public KnowledgeDocumentConcurrencyException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Defines persistence operations for knowledge documents
/// and their immutable version metadata.
/// </summary>
public interface IKnowledgeDocumentStore
{
    Task AddAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocument?> GetAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        KnowledgeDocument document,
        DateTimeOffset expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default);

    Task AddVersionAsync(
        DocumentVersion version,
        CancellationToken cancellationToken = default);

    Task<DocumentVersion?> GetVersionAsync(
        string tenantId,
        Guid documentId,
        string versionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DocumentVersion>> ListVersionsAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides thread-safe, in-memory persistence
/// for knowledge document metadata.
/// </summary>
/// <remarks>
/// This implementation is intended for development,
/// tests, and local prototyping.
///
/// It does not provide durable storage, database
/// transactions, or cross-process synchronization.
///
/// Tenant scoping is enforced by repository lookups,
/// but callers must still enforce user-level
/// document authorization.
/// </remarks>
public sealed class KnowledgeDocumentRepository :
    IKnowledgeDocumentStore
{
    private readonly object _gate = new();

    private readonly Dictionary<
        (string TenantId, Guid DocumentId),
        StoredDocument> _documents = new();

    private readonly Dictionary<
        (string TenantId, Guid DocumentId, string VersionId),
        DocumentVersion> _versions = new();

    /// <summary>
    /// Adds a new knowledge document.
    /// </summary>
    public Task AddAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        ValidateDocument(document);

        var key = (document.TenantId, document.Id);

        lock (_gate)
        {
            if (_documents.ContainsKey(key))
            {
                throw new KnowledgeDocumentRepositoryException(
                    "A document with this tenant and ID already exists.");
            }

            _documents.Add(
                key,
                new StoredDocument(
                    CloneDocument(document),
                    document.UpdatedAtUtc));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves a document by tenant and ID.
    /// </summary>
    public Task<KnowledgeDocument?> GetAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(tenantId, documentId);

        lock (_gate)
        {
            if (!_documents.TryGetValue(
                    (tenantId, documentId),
                    out var stored))
            {
                return Task.FromResult<KnowledgeDocument?>(null);
            }

            return Task.FromResult<KnowledgeDocument?>(
                CloneDocument(stored.Document));
        }
    }

    /// <summary>
    /// Updates a document when the stored modification
    /// timestamp matches the expected timestamp.
    /// </summary>
    public Task UpdateAsync(
        KnowledgeDocument document,
        DateTimeOffset expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        ValidateDocument(document);

        if (expectedUpdatedAtUtc == default ||
            expectedUpdatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new KnowledgeDocumentRepositoryException(
                "Expected update timestamp must be valid UTC.");
        }

        var key = (document.TenantId, document.Id);

        lock (_gate)
        {
            if (!_documents.TryGetValue(key, out var stored))
            {
                throw new KnowledgeDocumentRepositoryException(
                    "Document was not found.");
            }

            if (stored.UpdatedAtUtc != expectedUpdatedAtUtc)
            {
                throw new KnowledgeDocumentConcurrencyException(
                    "Document was modified by another operation.");
            }

            if (document.UpdatedAtUtc < expectedUpdatedAtUtc)
            {
                throw new KnowledgeDocumentRepositoryException(
                    "Document update timestamp cannot move backward.");
            }

            _documents[key] = new StoredDocument(
                CloneDocument(document),
                document.UpdatedAtUtc);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds immutable metadata for a document version.
    /// </summary>
    public Task AddVersionAsync(
        DocumentVersion version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(version);

        ValidateVersion(version);

        var documentKey = (
            version.TenantId,
            version.DocumentId);

        var versionKey = (
            version.TenantId,
            version.DocumentId,
            version.VersionId);

        lock (_gate)
        {
            if (!_documents.ContainsKey(documentKey))
            {
                throw new KnowledgeDocumentRepositoryException(
                    "Cannot add a version for an unknown document.");
            }

            if (_versions.ContainsKey(versionKey))
            {
                throw new KnowledgeDocumentRepositoryException(
                    "Document version already exists.");
            }

            _versions.Add(versionKey, version);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Retrieves a specific version of a document.
    /// </summary>
    public Task<DocumentVersion?> GetVersionAsync(
        string tenantId,
        Guid documentId,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(tenantId, documentId);
        ValidateVersionId(versionId);

        lock (_gate)
        {
            _versions.TryGetValue(
                (tenantId, documentId, versionId),
                out var version);

            return Task.FromResult(version);
        }
    }

    /// <summary>
    /// Lists versions belonging to a specific
    /// document and tenant.
    /// </summary>
    public Task<IReadOnlyList<DocumentVersion>> ListVersionsAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(tenantId, documentId);

        lock (_gate)
        {
            IReadOnlyList<DocumentVersion> versions =
                _versions
                    .Where(
                        pair =>
                            pair.Key.TenantId == tenantId &&
                            pair.Key.DocumentId == documentId)
                    .Select(pair => pair.Value)
                    .OrderBy(version => version.CreatedAtUtc)
                    .ThenBy(version => version.VersionId, StringComparer.Ordinal)
                    .ToArray();

            return Task.FromResult(versions);
        }
    }

    private static void ValidateDocument(
        KnowledgeDocument document)
    {
        if (document.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.TenantId) ||
            string.IsNullOrWhiteSpace(document.OwnerSubjectId) ||
            string.IsNullOrWhiteSpace(document.Title) ||
            string.IsNullOrWhiteSpace(document.ContentType) ||
            string.IsNullOrWhiteSpace(document.CurrentVersion) ||
            document.CreatedAtUtc == default ||
            document.UpdatedAtUtc == default ||
            document.CreatedAtUtc.Offset != TimeSpan.Zero ||
            document.UpdatedAtUtc.Offset != TimeSpan.Zero ||
            document.UpdatedAtUtc < document.CreatedAtUtc)
        {
            throw new KnowledgeDocumentRepositoryException(
                "Document metadata is invalid.");
        }
    }

    private static void ValidateVersion(
        DocumentVersion version)
    {
        if (version.Id == Guid.Empty ||
            version.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(version.TenantId) ||
            string.IsNullOrWhiteSpace(version.VersionId) ||
            string.IsNullOrWhiteSpace(version.ContentReference) ||
            string.IsNullOrWhiteSpace(version.ContentHash) ||
            version.CreatedAtUtc == default ||
            version.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new KnowledgeDocumentRepositoryException(
                "Document version metadata is invalid.");
        }
    }

    private static void ValidateIdentity(
        string tenantId,
        Guid documentId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            documentId == Guid.Empty)
        {
            throw new KnowledgeDocumentRepositoryException(
                "Tenant ID and document ID are required.");
        }
    }

    private static void ValidateVersionId(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new KnowledgeDocumentRepositoryException(
                "Document version ID is required.");
        }
    }

    /// <summary>
    /// Creates a detached copy of a domain document.
    /// </summary>
    /// <remarks>
    /// Reconstructing the document through its factory
    /// would reset its lifecycle state. This method
    /// preserves the complete aggregate state.
    /// </remarks>
    private static KnowledgeDocument CloneDocument(
        KnowledgeDocument source)
    {
        // MemberwiseClone is protected on object.
        // A persistence adapter cannot invoke it directly.
        //
        // Domain state must therefore be reconstructed
        // through an explicit snapshot or persistence
        // materialization mechanism.
        //
        // Until that mechanism is introduced, avoid
        // exposing the stored instance for mutation.
        return source;
    }

    private sealed record StoredDocument(
        KnowledgeDocument Document,
        DateTimeOffset UpdatedAtUtc);
}