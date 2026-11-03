namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Represents a request to retrieve stored document content.
/// </summary>
public sealed record GetDocumentContentRequest(
    Guid DocumentId,
    string TenantId);

/// <summary>
/// Defines the application operation for retrieving
/// document content.
/// </summary>
public interface IGetDocumentContent
{
    /// <summary>
    /// Retrieves document content for a tenant.
    /// </summary>
    /// <returns>
    /// A disposable content read result, or null when
    /// the document or its content does not exist.
    /// </returns>
    Task<DocumentContentReadResult?> ExecuteAsync(
        GetDocumentContentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Retrieves document content after validating
/// its document metadata and tenant scope.
/// </summary>
/// <remarks>
/// This operation does not authenticate callers
/// or enforce document-level access control.
///
/// The hosting application must verify the
/// authenticated caller's tenant membership
/// and permissions before invoking this operation.
///
/// The caller owns any returned content stream
/// and must dispose the result.
///
/// Content storage is provided through
/// IDocumentContentStore.
/// </remarks>
public sealed class GetDocumentContent : IGetDocumentContent
{
    private readonly IDocumentMetadataReader _metadataReader;
    private readonly IDocumentContentStore _contentStore;

    /// <summary>
    /// Creates a document content retrieval operation.
    /// </summary>
    public GetDocumentContent(
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
    public async Task<DocumentContentReadResult?> ExecuteAsync(
        GetDocumentContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequest(request);

        var tenantId = request.TenantId.Trim();

        // Confirm that the requested document exists
        // within the supplied tenant scope.
        var document = await _metadataReader.GetByIdAsync(
            request.DocumentId,
            tenantId,
            cancellationToken);

        if (document is null)
        {
            return null;
        }

        // Reject inconsistent metadata returned
        // by an incorrectly implemented repository.
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

        var key = new DocumentContentKey(
            TenantId: tenantId,
            DocumentId: request.DocumentId);

        // The storage implementation must enforce
        // tenant-scoped reads independently.
        var result = await _contentStore.ReadAsync(
            key,
            cancellationToken);

        if (result is null)
        {
            return null;
        }

        try
        {
            ValidateContentDescriptor(
                result.Descriptor,
                key);

            cancellationToken.ThrowIfCancellationRequested();

            return result;
        }
        catch
        {
            // The content stream belongs to this operation
            // until the validated result is returned.
            // Prevent leaks if validation fails.
            await result.DisposeAsync();

            throw;
        }
    }

    private static void ValidateRequest(
        GetDocumentContentRequest request)
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
    }

    private static void ValidateContentDescriptor(
        DocumentContentDescriptor descriptor,
        DocumentContentKey expectedKey)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (descriptor.DocumentId != expectedKey.DocumentId)
        {
            throw new InvalidOperationException(
                "The content store returned a different " +
                "document identifier.");
        }

        if (!string.Equals(
                descriptor.TenantId,
                expectedKey.TenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The content store returned content " +
                "outside the requested tenant scope.");
        }

        if (descriptor.Length < 0)
        {
            throw new InvalidOperationException(
                "The content store returned an invalid " +
                "content length.");
        }

        if (string.IsNullOrWhiteSpace(
                descriptor.ContentType))
        {
            throw new InvalidOperationException(
                "The content store returned an invalid " +
                "content type.");
        }

        if (string.IsNullOrWhiteSpace(
                descriptor.Version))
        {
            throw new InvalidOperationException(
                "The content store returned an invalid " +
                "content version.");
        }
    }
}