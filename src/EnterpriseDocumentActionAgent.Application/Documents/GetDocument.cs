using System.Collections.Concurrent;

namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Represents a request to retrieve document metadata.
/// </summary>
public sealed record GetDocumentRequest(
    Guid DocumentId,
    string TenantId);

/// <summary>
/// Represents document metadata returned to an application caller.
/// </summary>
public sealed record DocumentMetadata(
    Guid DocumentId,
    string TenantId,
    string Title,
    string FileName,
    string ContentType,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? Description = null);

/// <summary>
/// Defines a tenant-scoped document metadata reader.
/// </summary>
/// <remarks>
/// Implementations must ensure that document metadata
/// is retrieved only within the specified tenant.
///
/// The caller is responsible for authenticating the
/// requesting identity and enforcing document-level
/// access permissions.
/// </remarks>
public interface IDocumentMetadataReader
{
    /// <summary>
    /// Retrieves document metadata by document ID and tenant.
    /// </summary>
    Task<DocumentMetadata?> GetByIdAsync(
        Guid documentId,
        string tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the application operation for retrieving
/// document metadata.
/// </summary>
public interface IGetDocument
{
    /// <summary>
    /// Retrieves document metadata or returns null
    /// when the document is not found.
    /// </summary>
    Task<DocumentMetadata?> ExecuteAsync(
        GetDocumentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Retrieves tenant-scoped document metadata.
/// </summary>
public sealed class GetDocument : IGetDocument
{
    private readonly IDocumentMetadataReader _metadataReader;

    /// <summary>
    /// Creates a document retrieval operation.
    /// </summary>
    public GetDocument(
        IDocumentMetadataReader metadataReader)
    {
        _metadataReader = metadataReader
            ?? throw new ArgumentNullException(
                nameof(metadataReader));
    }

    /// <inheritdoc />
    public async Task<DocumentMetadata?> ExecuteAsync(
        GetDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.TenantId);

        var tenantId = request.TenantId.Trim();

        var document = await _metadataReader.GetByIdAsync(
            request.DocumentId,
            tenantId,
            cancellationToken);

        if (document is null)
        {
            return null;
        }

        // Fail closed if a reader accidentally returns
        // metadata belonging to another tenant.
        if (document.DocumentId != request.DocumentId ||
            !string.Equals(
                document.TenantId,
                tenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The document metadata reader returned " +
                "a record outside the requested scope.");
        }

        return document;
    }
}

/// <summary>
/// Provides a thread-safe, in-memory document metadata reader.
/// </summary>
/// <remarks>
/// This implementation is intended for development
/// and testing.
///
/// Data is lost when the application restarts.
///
/// Registration is not automatically connected to
/// CreateDocument. A shared repository will replace
/// this temporary implementation later.
/// </remarks>
public sealed class InMemoryDocumentMetadataReader
    : IDocumentMetadataReader
{
    private readonly ConcurrentDictionary<
        (string TenantId, Guid DocumentId),
        DocumentMetadata> _documents = new();

    /// <summary>
    /// Registers metadata for retrieval.
    /// </summary>
    /// <remarks>
    /// Duplicate tenant/document keys are rejected.
    /// </remarks>
    public void Register(DocumentMetadata document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.DocumentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(document));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            document.TenantId);

        var tenantId = document.TenantId.Trim();

        if (!string.Equals(
                document.TenantId,
                tenantId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Document tenant identifiers must be normalized.",
                nameof(document));
        }

        var key = (
            TenantId: tenantId,
            DocumentId: document.DocumentId);

        if (!_documents.TryAdd(key, document))
        {
            throw new InvalidOperationException(
                "Document metadata is already registered.");
        }
    }

    /// <inheritdoc />
    public Task<DocumentMetadata?> GetByIdAsync(
        Guid documentId,
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (documentId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid document identifier is required.",
                nameof(documentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            tenantId);

        var key = (
            TenantId: tenantId.Trim(),
            DocumentId: documentId);

        _documents.TryGetValue(
            key,
            out var document);

        return Task.FromResult(document);
    }
}