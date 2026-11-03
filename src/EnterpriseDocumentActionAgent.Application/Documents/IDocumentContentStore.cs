namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Identifies a document's content within a tenant.
/// </summary>
public sealed record DocumentContentKey(
    string TenantId,
    Guid DocumentId);

/// <summary>
/// Represents a stored document-content version.
/// </summary>
public sealed record DocumentContentDescriptor(
    Guid DocumentId,
    string TenantId,
    string ContentType,
    long Length,
    string Version,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Represents a request to write document content.
/// </summary>
/// <remarks>
/// ExpectedVersion is used for optimistic concurrency.
///
/// A null ExpectedVersion means the caller expects
/// that no content currently exists.
///
/// A non-null ExpectedVersion must match the
/// currently stored content version.
///
/// Implementations must enforce these conditions
/// atomically to prevent lost updates.
/// </remarks>
public sealed record WriteDocumentContentRequest(
    DocumentContentKey Key,
    Stream Content,
    string ContentType,
    string? ExpectedVersion = null);

/// <summary>
/// Represents a request to delete document content.
/// </summary>
/// <remarks>
/// Deletion requires an exact expected version.
/// Implementations must reject stale versions.
/// </remarks>
public sealed record DeleteDocumentContentRequest(
    DocumentContentKey Key,
    string ExpectedVersion);

/// <summary>
/// Represents a document-content read result.
/// </summary>
/// <remarks>
/// The caller owns the returned stream and must dispose it.
/// </remarks>
public sealed class DocumentContentReadResult : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Creates a content read result.
    /// </summary>
    public DocumentContentReadResult(
        DocumentContentDescriptor descriptor,
        Stream content)
    {
        Descriptor = descriptor
            ?? throw new ArgumentNullException(nameof(descriptor));

        Content = content
            ?? throw new ArgumentNullException(nameof(content));
    }

    /// <summary>
    /// Describes the stored document content.
    /// </summary>
    public DocumentContentDescriptor Descriptor { get; }

    /// <summary>
    /// Provides access to the stored content stream.
    /// </summary>
    public Stream Content { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Content.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return Content.DisposeAsync();
    }
}

/// <summary>
/// Defines storage operations for document content.
/// </summary>
/// <remarks>
/// This interface does not authenticate callers or
/// authorize access to tenant documents.
///
/// Implementations must enforce tenant-scoped storage
/// and atomic version checks.
///
/// Application services must independently verify
/// authenticated identities, document ownership,
/// permissions, and approval requirements.
/// </remarks>
public interface IDocumentContentStore
{
    /// <summary>
    /// Saves new content or replaces an existing version.
    /// </summary>
    /// <remarks>
    /// A null expected version requires content to be absent.
    /// A matching expected version permits replacement.
    /// </remarks>
    Task<DocumentContentDescriptor> WriteAsync(
        WriteDocumentContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves document content for a tenant.
    /// </summary>
    /// <returns>
    /// The content and descriptor, or null when absent.
    /// </returns>
    Task<DocumentContentReadResult?> ReadAsync(
        DocumentContentKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether document content exists.
    /// </summary>
    Task<bool> ExistsAsync(
        DocumentContentKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes content only when its version matches.
    /// </summary>
    /// <returns>
    /// True when deleted; false when content is absent.
    /// </returns>
    Task<bool> DeleteAsync(
        DeleteDocumentContentRequest request,
        CancellationToken cancellationToken = default);
}