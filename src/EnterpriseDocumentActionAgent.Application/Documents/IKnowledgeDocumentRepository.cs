using EnterpriseDocumentActionAgent.Domain.Documents;

namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Represents an expected concurrency state for
/// a knowledge document aggregate.
/// </summary>
/// <remarks>
/// The expected update timestamp must be obtained
/// from a previously retrieved document.
///
/// Repository implementations must compare the
/// expected value with the persisted value atomically.
/// </remarks>
public sealed record KnowledgeDocumentConcurrencyToken(
    DateTimeOffset ExpectedUpdatedAtUtc);

/// <summary>
/// Represents a knowledge document persistence failure.
/// </summary>
public class KnowledgeDocumentPersistenceException : Exception
{
    public KnowledgeDocumentPersistenceException(string message)
        : base(message)
    {
    }

    public KnowledgeDocumentPersistenceException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Represents an optimistic concurrency conflict
/// during a knowledge document update.
/// </summary>
public sealed class KnowledgeDocumentVersionConflictException
    : KnowledgeDocumentPersistenceException
{
    public KnowledgeDocumentVersionConflictException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Defines application-facing persistence operations
/// for knowledge documents and their versions.
/// </summary>
/// <remarks>
/// Implementations must enforce tenant isolation.
///
/// The tenant identifier must be derived from a
/// trusted authenticated identity before application
/// services invoke these operations.
///
/// Repository-level tenant filtering does not replace
/// document ownership or permission checks.
///
/// Implementations must also prevent external mutation
/// of stored aggregates through shared references.
/// </remarks>
public interface IKnowledgeDocumentRepository
{
    /// <summary>
    /// Persists a newly created knowledge document.
    /// </summary>
    /// <remarks>
    /// The operation must fail if a document with the
    /// same identity already exists in the tenant.
    /// </remarks>
    Task AddAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a document within the specified tenant.
    /// </summary>
    /// <returns>
    /// The document aggregate, or null when it does
    /// not exist within the requested tenant.
    /// </returns>
    Task<KnowledgeDocument?> GetByIdAsync(
        Guid documentId,
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists an updated document aggregate.
    /// </summary>
    /// <remarks>
    /// Implementations must compare the expected
    /// timestamp against the stored timestamp
    /// atomically before committing the update.
    ///
    /// A mismatch must raise
    /// KnowledgeDocumentVersionConflictException.
    ///
    /// The update must not modify the document identity,
    /// tenant identity, or creation timestamp.
    /// </remarks>
    Task UpdateAsync(
        KnowledgeDocument document,
        KnowledgeDocumentConcurrencyToken concurrencyToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists metadata for a new immutable
    /// version of an existing document.
    /// </summary>
    /// <remarks>
    /// The repository must reject duplicate version
    /// identifiers within the same document and tenant.
    ///
    /// The document must already exist in the
    /// same tenant.
    /// </remarks>
    Task AddVersionAsync(
        DocumentVersion version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a specific version of a document.
    /// </summary>
    Task<DocumentVersion?> GetVersionAsync(
        Guid documentId,
        string tenantId,
        string versionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the immutable versions associated
    /// with a document in a tenant.
    /// </summary>
    /// <remarks>
    /// Implementations should return versions
    /// ordered by creation timestamp and then
    /// by version identifier.
    ///
    /// The returned collection must not expose
    /// mutable internal repository state.
    /// </remarks>
    Task<IReadOnlyList<DocumentVersion>> ListVersionsAsync(
        Guid documentId,
        string tenantId,
        CancellationToken cancellationToken = default);
}