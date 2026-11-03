namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Represents a request to create or replace document content.
/// </summary>
/// <remarks>
/// ExpectedVersion controls optimistic concurrency.
///
/// A null ExpectedVersion means that content must not
/// already exist.
///
/// A non-null ExpectedVersion means that the stored
/// content must match that exact version.
///
/// The storage implementation must enforce these
/// conditions atomically.
/// </remarks>
public sealed record UpdateDocumentContentRequest(
    Guid DocumentId,
    string TenantId,
    Stream Content,
    string ContentType,
    string? ExpectedVersion = null);

/// <summary>
/// Defines the application operation for writing
/// document content.
/// </summary>
public interface IUpdateDocumentContent
{
    /// <summary>
    /// Writes document content and returns its new descriptor.
    /// </summary>
    Task<DocumentContentDescriptor> ExecuteAsync(
        UpdateDocumentContentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates and coordinates document-content writes.
/// </summary>
/// <remarks>
/// This service does not authenticate callers,
/// authorize document modifications, or verify
/// human approval decisions.
///
/// Those controls must be enforced by the hosting
/// application before this operation is invoked.
///
/// The content stream remains owned by the caller.
/// This operation does not dispose it.
///
/// Actual persistence and atomic version checks
/// are delegated to IDocumentContentStore.
/// </remarks>
public sealed class UpdateDocumentContent
    : IUpdateDocumentContent
{
    private readonly IDocumentMetadataReader _metadataReader;
    private readonly IDocumentContentStore _contentStore;

    /// <summary>
    /// Creates a document-content update operation.
    /// </summary>
    public UpdateDocumentContent(
        IDocumentMetadataReader metadataReader,
        IDocumentContentStore contentStore)
    {
        _metadataReader = metadataReader
            ?? throw new ArgumentNullException(
                nameof(metadataReader));

        _contentStore = contentStore
            ?? throw new ArgumentNullException(
                nameof(contentStore));
    }

    /// <inheritdoc />
    public async Task<DocumentContentDescriptor> ExecuteAsync(
        UpdateDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequest(request);

        var tenantId = request.TenantId.Trim();
        var contentType = request.ContentType.Trim();

        // Verify the document exists in the requested tenant.
        var document = await _metadataReader.GetByIdAsync(
            request.DocumentId,
            tenantId,
            cancellationToken);

        if (document is null)
        {
            throw new KeyNotFoundException(
                "The document was not found.");
        }

        // Fail closed if the metadata reader returns
        // an unexpected document or tenant.
        if (document.DocumentId != request.DocumentId ||
            !string.Equals(
                document.TenantId,
                tenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The metadata reader returned a document " +
                "outside the requested scope.");
        }

        var key = new DocumentContentKey(
            TenantId: tenantId,
            DocumentId: request.DocumentId);

        // The content store is responsible for atomic
        // optimistic-concurrency enforcement.
        var writeRequest = new WriteDocumentContentRequest(
            Key: key,
            Content: request.Content,
            ContentType: contentType,
            ExpectedVersion: request.ExpectedVersion);

        var descriptor = await _contentStore.WriteAsync(
            writeRequest,
            cancellationToken);

        ValidateDescriptor(
            descriptor,
            key,
            contentType);

        return descriptor;
    }

    private static void ValidateRequest(
        UpdateDocumentContentRequest request)
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

        ArgumentNullException.ThrowIfNull(
            request.Content);

        if (!request.Content.CanRead)
        {
            throw new ArgumentException(
                "The content stream must be readable.",
                nameof(request));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.ContentType);

        if (request.ContentType.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Content type cannot exceed 128 characters.",
                nameof(request));
        }

        if (request.ContentType.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Content type cannot contain control characters.",
                nameof(request));
        }

        if (request.ExpectedVersion is not null)
        {
            if (string.IsNullOrWhiteSpace(
                    request.ExpectedVersion))
            {
                throw new ArgumentException(
                    "Expected version cannot be empty.",
                    nameof(request));
            }

            if (request.ExpectedVersion.Length > 256)
            {
                throw new ArgumentException(
                    "Expected version cannot exceed 256 characters.",
                    nameof(request));
            }
        }
    }

    private static void ValidateDescriptor(
        DocumentContentDescriptor descriptor,
        DocumentContentKey expectedKey,
        string expectedContentType)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.DocumentId != expectedKey.DocumentId)
        {
            throw new InvalidOperationException(
                "The content store returned an unexpected " +
                "document identifier.");
        }

        if (!string.Equals(
                descriptor.TenantId,
                expectedKey.TenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The content store returned an unexpected " +
                "tenant identifier.");
        }

        if (!string.Equals(
                descriptor.ContentType,
                expectedContentType,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The content store returned an unexpected " +
                "content type.");
        }

        if (descriptor.Length < 0)
        {
            throw new InvalidOperationException(
                "The content store returned an invalid " +
                "content length.");
        }

        if (string.IsNullOrWhiteSpace(
                descriptor.Version))
        {
            throw new InvalidOperationException(
                "The content store returned an invalid " +
                "content version.");
        }

        if (descriptor.UpdatedAtUtc == default)
        {
            throw new InvalidOperationException(
                "The content store returned an invalid " +
                "update timestamp.");
        }
    }
}