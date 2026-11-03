namespace EnterpriseDocumentActionAgent.Application.Documents;

/// <summary>
/// Represents a request to register a new document.
/// </summary>
public sealed record CreateDocumentRequest(
    string TenantId,
    string Title,
    string FileName,
    string ContentType,
    string CreatedBy,
    string? Description = null);

/// <summary>
/// Represents the result of creating document metadata.
/// </summary>
public sealed record CreateDocumentResult(
    Guid DocumentId,
    string TenantId,
    string Title,
    string FileName,
    string ContentType,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? Description);

/// <summary>
/// Defines an application operation for creating documents.
/// </summary>
public interface ICreateDocument
{
    /// <summary>
    /// Validates a document registration request and
    /// creates a new document metadata result.
    /// </summary>
    Task<CreateDocumentResult> ExecuteAsync(
        CreateDocumentRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates validated document metadata registrations.
/// </summary>
/// <remarks>
/// This implementation does not persist metadata,
/// upload content, or publish ingestion events.
///
/// Tenant identifiers and creator identities must be
/// obtained from trusted authentication infrastructure.
///
/// Repository persistence will be integrated later.
/// </remarks>
public sealed class CreateDocument : ICreateDocument
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Creates a document registration operation.
    /// </summary>
    public CreateDocument(
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<CreateDocumentResult> ExecuteAsync(
        CreateDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequest(request);

        var documentId = Guid.NewGuid();

        var createdAtUtc = _timeProvider.GetUtcNow();

        var result = new CreateDocumentResult(
            DocumentId: documentId,
            TenantId: request.TenantId.Trim(),
            Title: request.Title.Trim(),
            FileName: request.FileName.Trim(),
            ContentType: request.ContentType.Trim(),
            CreatedBy: request.CreatedBy.Trim(),
            CreatedAtUtc: createdAtUtc,
            Description: NormalizeOptionalText(
                request.Description));

        return Task.FromResult(result);
    }

    private static void ValidateRequest(
        CreateDocumentRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.TenantId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Title);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.FileName);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.ContentType);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.CreatedBy);

        if (request.TenantId.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Tenant identifier cannot exceed 128 characters.",
                nameof(request));
        }

        if (request.Title.Trim().Length > 256)
        {
            throw new ArgumentException(
                "Document title cannot exceed 256 characters.",
                nameof(request));
        }

        if (request.FileName.Trim().Length > 255)
        {
            throw new ArgumentException(
                "File name cannot exceed 255 characters.",
                nameof(request));
        }

        if (request.ContentType.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Content type cannot exceed 128 characters.",
                nameof(request));
        }

        if (request.CreatedBy.Trim().Length > 128)
        {
            throw new ArgumentException(
                "Creator identifier cannot exceed 128 characters.",
                nameof(request));
        }

        if (request.Description is not null &&
            request.Description.Trim().Length > 2000)
        {
            throw new ArgumentException(
                "Document description cannot exceed 2000 characters.",
                nameof(request));
        }

        ValidateFileName(request.FileName.Trim());
    }

    private static void ValidateFileName(
        string fileName)
    {
        if (fileName is "." or "..")
        {
            throw new ArgumentException(
                "A valid document file name is required.",
                nameof(fileName));
        }

        // File names are metadata, not storage paths.
        // Reject directory separators and control characters.
        if (fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.Any(char.IsControl))
        {
            throw new ArgumentException(
                "File name cannot contain path separators " +
                "or control characters.",
                nameof(fileName));
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