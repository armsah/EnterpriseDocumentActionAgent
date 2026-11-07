namespace EnterpriseDocumentActionAgent.Infrastructure.Persistence;

/// <summary>
/// Represents a persisted knowledge document record.
/// </summary>
public sealed record PersistedKnowledgeDocument(
    Guid DocumentId,
    string TenantId,
    string OwnerSubjectId,
    string Title,
    string ContentType,
    string CurrentVersion,
    string Status,
    long Revision,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Represents a persisted document version record.
/// </summary>
public sealed record PersistedDocumentVersion(
    Guid VersionRecordId,
    Guid DocumentId,
    string TenantId,
    string VersionId,
    string ContentType,
    string ContentReference,
    long ContentLength,
    string ContentHash,
    DateTimeOffset CreatedAtUtc);

/// <summary>
/// Represents a persistence context concurrency failure.
/// </summary>
public sealed class DocumentPersistenceConcurrencyException : Exception
{
    public DocumentPersistenceConcurrencyException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Represents a persistence context validation failure.
/// </summary>
public sealed class DocumentPersistenceException : Exception
{
    public DocumentPersistenceException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Defines tenant-scoped persistence operations for
/// document and workflow records.
/// </summary>
public interface IEnterpriseDocumentActionAgentDataContext
{
    Task<PersistedKnowledgeDocument> InsertDocumentAsync(
        PersistedKnowledgeDocument document,
        CancellationToken cancellationToken = default);

    Task<PersistedKnowledgeDocument?> FindDocumentAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<PersistedKnowledgeDocument> UpdateDocumentAsync(
        PersistedKnowledgeDocument document,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task InsertDocumentVersionAsync(
        PersistedDocumentVersion version,
        CancellationToken cancellationToken = default);

    Task<PersistedDocumentVersion?> FindDocumentVersionAsync(
        string tenantId,
        Guid documentId,
        string versionId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersistedDocumentVersion>> ListDocumentVersionsAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task InsertWorkflowAsync(
        StoredWorkflowSnapshot workflow,
        CancellationToken cancellationToken = default);

    Task<StoredWorkflowSnapshot?> FindWorkflowAsync(
        string tenantId,
        Guid workflowId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides an in-memory implementation of the
/// enterprise document persistence context.
/// </summary>
/// <remarks>
/// This class does not inherit from Entity Framework
/// Core DbContext.
///
/// It is a provider-independent development adapter.
///
/// Operations are synchronized within one process,
/// but they are not durable and do not participate
/// in database transactions.
///
/// Production implementations must provide database
/// constraints, migrations, transactional consistency,
/// and tenant-scoped authorization.
/// </remarks>
public sealed class EnterpriseDocumentActionAgentDbContext :
    IEnterpriseDocumentActionAgentDataContext
{
    private const int MaximumTenantIdLength = 200;
    private const int MaximumTitleLength = 300;
    private const int MaximumContentTypeLength = 150;
    private const int MaximumVersionLength = 100;
    private const int MaximumWorkflowStateLength = 1000000;

    private readonly object _gate = new();

    private readonly Dictionary<
        (string TenantId, Guid DocumentId),
        PersistedKnowledgeDocument> _documents = new();

    private readonly Dictionary<
        (string TenantId, Guid DocumentId, string VersionId),
        PersistedDocumentVersion> _versions = new();

    private readonly Dictionary<
        (string TenantId, Guid WorkflowId),
        StoredWorkflowSnapshot> _workflows = new();

    /// <summary>
    /// Inserts a new document record.
    /// </summary>
    public Task<PersistedKnowledgeDocument> InsertDocumentAsync(
        PersistedKnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateDocument(document);

        if (document.Revision != 0)
        {
            throw new DocumentPersistenceException(
                "New documents must begin at revision zero.");
        }

        var key = (document.TenantId, document.DocumentId);

        lock (_gate)
        {
            if (_documents.ContainsKey(key))
            {
                throw new DocumentPersistenceException(
                    "Document already exists.");
            }

            _documents.Add(key, document);
        }

        return Task.FromResult(document);
    }

    /// <summary>
    /// Finds a document using its tenant and identity.
    /// </summary>
    public Task<PersistedKnowledgeDocument?> FindDocumentAsync(
        string tenantId,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(tenantId, documentId);

        lock (_gate)
        {
            _documents.TryGetValue(
                (tenantId, documentId),
                out var document);

            return Task.FromResult(document);
        }
    }

    /// <summary>
    /// Updates a document using optimistic concurrency.
    /// </summary>
    public Task<PersistedKnowledgeDocument> UpdateDocumentAsync(
        PersistedKnowledgeDocument document,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateDocument(document);

        if (expectedRevision < 0)
        {
            throw new DocumentPersistenceException(
                "Expected revision cannot be negative.");
        }

        var key = (document.TenantId, document.DocumentId);

        lock (_gate)
        {
            if (!_documents.TryGetValue(key, out var existing))
            {
                throw new DocumentPersistenceException(
                    "Document does not exist.");
            }

            if (existing.Revision != expectedRevision)
            {
                throw new DocumentPersistenceConcurrencyException(
                    "Document was modified by another operation.");
            }

            if (document.Revision != expectedRevision)
            {
                throw new DocumentPersistenceException(
                    "Submitted revision must match expected revision.");
            }

            if (document.CreatedAtUtc != existing.CreatedAtUtc ||
                !string.Equals(
                    document.OwnerSubjectId,
                    existing.OwnerSubjectId,
                    StringComparison.Ordinal))
            {
                throw new DocumentPersistenceException(
                    "Immutable document metadata cannot be changed.");
            }

            if (document.UpdatedAtUtc < existing.UpdatedAtUtc)
            {
                throw new DocumentPersistenceException(
                    "Document timestamp cannot move backward.");
            }

            if (expectedRevision == long.MaxValue)
            {
                throw new DocumentPersistenceException(
                    "Document revision cannot be incremented further.");
            }

            var updated = document with
            {
                Revision = expectedRevision + 1
            };

            _documents[key] = updated;

            return Task.FromResult(updated);
        }
    }

    /// <summary>
    /// Inserts immutable document version metadata.
    /// </summary>
    public Task InsertDocumentVersionAsync(
        PersistedDocumentVersion version,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
                throw new DocumentPersistenceException(
                    "Cannot add a version for an unknown document.");
            }

            if (_versions.ContainsKey(versionKey))
            {
                throw new DocumentPersistenceException(
                    "Document version already exists.");
            }

            _versions.Add(versionKey, version);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds a specific document version.
    /// </summary>
    public Task<PersistedDocumentVersion?> FindDocumentVersionAsync(
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
    /// Lists document versions for a tenant and document.
    /// </summary>
    public Task<IReadOnlyList<PersistedDocumentVersion>>
        ListDocumentVersionsAsync(
            string tenantId,
            Guid documentId,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(tenantId, documentId);

        lock (_gate)
        {
            IReadOnlyList<PersistedDocumentVersion> versions =
                _versions
                    .Where(
                        item =>
                            item.Key.TenantId == tenantId &&
                            item.Key.DocumentId == documentId)
                    .Select(item => item.Value)
                    .OrderBy(item => item.CreatedAtUtc)
                    .ThenBy(item => item.VersionId, StringComparer.Ordinal)
                    .ToArray();

            return Task.FromResult(versions);
        }
    }

    /// <summary>
    /// Inserts a serialized workflow snapshot.
    /// </summary>
    public Task InsertWorkflowAsync(
        StoredWorkflowSnapshot workflow,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateWorkflow(workflow);

        if (workflow.Revision != 0)
        {
            throw new DocumentPersistenceException(
                "New workflows must begin at revision zero.");
        }

        var key = (workflow.TenantId, workflow.WorkflowId);

        lock (_gate)
        {
            if (_workflows.ContainsKey(key))
            {
                throw new DocumentPersistenceException(
                    "Workflow already exists.");
            }

            _workflows.Add(key, workflow);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds a workflow snapshot by tenant and ID.
    /// </summary>
    public Task<StoredWorkflowSnapshot?> FindWorkflowAsync(
        string tenantId,
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateIdentity(tenantId, workflowId);

        lock (_gate)
        {
            _workflows.TryGetValue(
                (tenantId, workflowId),
                out var workflow);

            return Task.FromResult(workflow);
        }
    }

    private static void ValidateDocument(
        PersistedKnowledgeDocument? document)
    {
        if (document is null ||
            document.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.OwnerSubjectId) ||
            string.IsNullOrWhiteSpace(document.Title) ||
            document.Title.Length > MaximumTitleLength ||
            string.IsNullOrWhiteSpace(document.ContentType) ||
            document.ContentType.Length > MaximumContentTypeLength ||
            string.IsNullOrWhiteSpace(document.CurrentVersion) ||
            document.CurrentVersion.Length > MaximumVersionLength ||
            string.IsNullOrWhiteSpace(document.Status) ||
            document.Revision < 0)
        {
            throw new DocumentPersistenceException(
                "Document metadata is invalid.");
        }

        ValidateTenantId(document.TenantId);
        ValidateTimestamp(document.CreatedAtUtc);
        ValidateTimestamp(document.UpdatedAtUtc);

        if (document.UpdatedAtUtc < document.CreatedAtUtc)
        {
            throw new DocumentPersistenceException(
                "Document timestamps are inconsistent.");
        }
    }

    private static void ValidateVersion(
        PersistedDocumentVersion? version)
    {
        if (version is null ||
            version.VersionRecordId == Guid.Empty ||
            version.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(version.VersionId) ||
            version.VersionId.Length > MaximumVersionLength ||
            string.IsNullOrWhiteSpace(version.ContentType) ||
            version.ContentType.Length > MaximumContentTypeLength ||
            string.IsNullOrWhiteSpace(version.ContentReference) ||
            version.ContentLength < 0 ||
            string.IsNullOrWhiteSpace(version.ContentHash))
        {
            throw new DocumentPersistenceException(
                "Document version metadata is invalid.");
        }

        ValidateTenantId(version.TenantId);
        ValidateTimestamp(version.CreatedAtUtc);
    }

    private static void ValidateWorkflow(
        StoredWorkflowSnapshot? workflow)
    {
        if (workflow is null ||
            workflow.WorkflowId == Guid.Empty ||
            string.IsNullOrWhiteSpace(workflow.WorkflowState) ||
            workflow.WorkflowState.Length > MaximumWorkflowStateLength ||
            workflow.Revision < 0)
        {
            throw new DocumentPersistenceException(
                "Workflow metadata is invalid.");
        }

        ValidateTenantId(workflow.TenantId);
        ValidateTimestamp(workflow.CreatedAtUtc);
        ValidateTimestamp(workflow.UpdatedAtUtc);

        if (workflow.UpdatedAtUtc < workflow.CreatedAtUtc)
        {
            throw new DocumentPersistenceException(
                "Workflow timestamps are inconsistent.");
        }
    }

    private static void ValidateIdentity(
        string tenantId,
        Guid id)
    {
        ValidateTenantId(tenantId);

        if (id == Guid.Empty)
        {
            throw new DocumentPersistenceException(
                "Entity ID must not be empty.");
        }
    }

    private static void ValidateVersionId(string? versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId) ||
            versionId.Length > MaximumVersionLength)
        {
            throw new DocumentPersistenceException(
                "Version ID is invalid.");
        }
    }

    private static void ValidateTenantId(string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) ||
            tenantId.Length > MaximumTenantIdLength)
        {
            throw new DocumentPersistenceException(
                "Tenant ID is invalid.");
        }
    }

    private static void ValidateTimestamp(
        DateTimeOffset timestamp)
    {
        if (timestamp == default ||
            timestamp.Offset != TimeSpan.Zero)
        {
            throw new DocumentPersistenceException(
                "Timestamp must be a valid UTC value.");
        }
    }
}