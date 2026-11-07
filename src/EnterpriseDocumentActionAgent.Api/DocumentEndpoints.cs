using EnterpriseDocumentActionAgent.Application.Grounding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EnterpriseDocumentActionAgent.Api;

/// <summary>
/// Represents a request to create a document.
/// </summary>
/// <remarks>
/// Tenant and owner identities are resolved from
/// authenticated server-side context.
/// </remarks>
public sealed record CreateDocumentHttpRequest(
    string Title,
    string ContentType,
    string Content);

/// <summary>
/// Represents document metadata returned by
/// an authorized document application service.
/// </summary>
public sealed record AuthorizedDocumentRecord(
    Guid DocumentId,
    string TenantId,
    string OwnerSubjectId,
    string Title,
    string ContentType,
    string Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Represents document metadata exposed to
/// an authenticated HTTP client.
/// </summary>
public sealed record DocumentHttpResponse(
    Guid DocumentId,
    string Title,
    string ContentType,
    string Version,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Describes the outcome of an authorized
/// document deletion operation.
/// </summary>
public enum AuthorizedDocumentDeleteOutcome
{
    Deleted = 1,
    NotFound = 2,
    ApprovalRequired = 3
}

/// <summary>
/// Defines application operations for documents
/// accessible to an authenticated caller.
/// </summary>
/// <remarks>
/// Implementations must enforce:
///
/// - Tenant isolation.
/// - Document-level access authorization.
/// - Ownership and role-based permissions.
/// - Content validation and storage limits.
/// - Appropriate deletion approval requirements.
/// - Consistent document versioning.
/// - Durable storage and event publication.
///
/// This interface does not implement those controls.
/// </remarks>
public interface IAuthorizedDocumentApplicationService
{
    Task<AuthorizedDocumentRecord> CreateAsync(
        GroundedQuestionRequestIdentity identity,
        string title,
        string contentType,
        string content,
        CancellationToken cancellationToken = default);

    Task<AuthorizedDocumentRecord?> GetAsync(
        GroundedQuestionRequestIdentity identity,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<AuthorizedDocumentDeleteOutcome> DeleteAsync(
        GroundedQuestionRequestIdentity identity,
        Guid documentId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines HTTP endpoints for authorized
/// enterprise document operations.
/// </summary>
public static class DocumentEndpoints
{
    private const int MaximumTitleCharacters = 300;
    private const int MaximumContentTypeCharacters = 150;
    private const int MaximumContentCharacters = 100000;

    /// <summary>
    /// Registers document management endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapDocumentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
                "/api/documents",
                HandleCreateAsync)
            .WithName("CreateDocument")
            .WithTags("Documents")
            .Produces<DocumentHttpResponse>(
                StatusCodes.Status201Created)
            .Produces(
                StatusCodes.Status400BadRequest)
            .Produces(
                StatusCodes.Status401Unauthorized)
            .Produces(
                StatusCodes.Status403Forbidden)
            .Produces(
                StatusCodes.Status503ServiceUnavailable);

        endpoints.MapGet(
                "/api/documents/{documentId:guid}",
                HandleGetAsync)
            .WithName("GetDocument")
            .WithTags("Documents")
            .Produces<DocumentHttpResponse>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status401Unauthorized)
            .Produces(
                StatusCodes.Status403Forbidden)
            .Produces(
                StatusCodes.Status404NotFound)
            .Produces(
                StatusCodes.Status503ServiceUnavailable);

        endpoints.MapDelete(
                "/api/documents/{documentId:guid}",
                HandleDeleteAsync)
            .WithName("DeleteDocument")
            .WithTags("Documents")
            .Produces(
                StatusCodes.Status204NoContent)
            .Produces(
                StatusCodes.Status401Unauthorized)
            .Produces(
                StatusCodes.Status403Forbidden)
            .Produces(
                StatusCodes.Status404NotFound)
            .Produces(
                StatusCodes.Status409Conflict)
            .Produces(
                StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateDocumentHttpRequest request,
        HttpContext httpContext,
        IGroundedQuestionRequestIdentityProvider identityProvider,
        IAuthorizedDocumentApplicationService documentService,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Title) ||
            request.Title.Length > MaximumTitleCharacters ||
            string.IsNullOrWhiteSpace(request.ContentType) ||
            request.ContentType.Length > MaximumContentTypeCharacters ||
            string.IsNullOrWhiteSpace(request.Content) ||
            request.Content.Length > MaximumContentCharacters)
        {
            return Results.BadRequest(
                new
                {
                    error = "Invalid document creation request."
                });
        }

        var identity = await ResolveIdentityAsync(
            identityProvider,
            httpContext,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        AuthorizedDocumentRecord created;

        try
        {
            created = await documentService.CreateAsync(
                identity,
                request.Title,
                request.ContentType,
                request.Content,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        if (!IsValidDocument(created, identity.TenantId))
        {
            return Results.Problem(
                "Document creation returned invalid metadata.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Created(
            $"/api/documents/{created.DocumentId}",
            ToHttpResponse(created));
    }

    private static async Task<IResult> HandleGetAsync(
        Guid documentId,
        HttpContext httpContext,
        IGroundedQuestionRequestIdentityProvider identityProvider,
        IAuthorizedDocumentApplicationService documentService,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            return Results.BadRequest(
                new
                {
                    error = "Document ID must not be empty."
                });
        }

        var identity = await ResolveIdentityAsync(
            identityProvider,
            httpContext,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        AuthorizedDocumentRecord? document;

        try
        {
            document = await documentService.GetAsync(
                identity,
                documentId,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        if (document is null)
        {
            return Results.NotFound();
        }

        if (!IsValidDocument(document, identity.TenantId) ||
            document.DocumentId != documentId)
        {
            return Results.Problem(
                "Document retrieval returned invalid metadata.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(ToHttpResponse(document));
    }

    private static async Task<IResult> HandleDeleteAsync(
        Guid documentId,
        HttpContext httpContext,
        IGroundedQuestionRequestIdentityProvider identityProvider,
        IAuthorizedDocumentApplicationService documentService,
        CancellationToken cancellationToken)
    {
        if (documentId == Guid.Empty)
        {
            return Results.BadRequest(
                new
                {
                    error = "Document ID must not be empty."
                });
        }

        var identity = await ResolveIdentityAsync(
            identityProvider,
            httpContext,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        AuthorizedDocumentDeleteOutcome outcome;

        try
        {
            outcome = await documentService.DeleteAsync(
                identity,
                documentId,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        return outcome switch
        {
            AuthorizedDocumentDeleteOutcome.Deleted =>
                Results.NoContent(),

            AuthorizedDocumentDeleteOutcome.NotFound =>
                Results.NotFound(),

            AuthorizedDocumentDeleteOutcome.ApprovalRequired =>
                Results.Conflict(
                    new
                    {
                        error =
                            "Document deletion requires approval."
                    }),

            _ => Results.Problem(
                "Document deletion returned an unsupported outcome.",
                statusCode:
                    StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static async Task<GroundedQuestionRequestIdentity?>
        ResolveIdentityAsync(
            IGroundedQuestionRequestIdentityProvider identityProvider,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        var identity = await identityProvider.GetIdentityAsync(
            httpContext,
            cancellationToken);

        if (identity is null ||
            string.IsNullOrWhiteSpace(identity.TenantId) ||
            string.IsNullOrWhiteSpace(identity.SubjectId))
        {
            return null;
        }

        return identity;
    }

    private static bool IsValidDocument(
        AuthorizedDocumentRecord? document,
        string expectedTenantId)
    {
        if (document is null ||
            document.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.TenantId) ||
            string.IsNullOrWhiteSpace(document.OwnerSubjectId) ||
            string.IsNullOrWhiteSpace(document.Title) ||
            string.IsNullOrWhiteSpace(document.ContentType) ||
            string.IsNullOrWhiteSpace(document.Version) ||
            document.CreatedAtUtc == default ||
            document.UpdatedAtUtc == default)
        {
            return false;
        }

        if (!string.Equals(
                document.TenantId,
                expectedTenantId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (document.Title.Length > MaximumTitleCharacters ||
            document.ContentType.Length > MaximumContentTypeCharacters)
        {
            return false;
        }

        if (document.UpdatedAtUtc < document.CreatedAtUtc)
        {
            return false;
        }

        return true;
    }

    private static DocumentHttpResponse ToHttpResponse(
        AuthorizedDocumentRecord document)
    {
        return new DocumentHttpResponse(
            document.DocumentId,
            document.Title,
            document.ContentType,
            document.Version,
            document.CreatedAtUtc,
            document.UpdatedAtUtc);
    }
}