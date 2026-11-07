using EnterpriseDocumentActionAgent.Application.Grounding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EnterpriseDocumentActionAgent.Api;

/// <summary>
/// Represents a document-search request submitted
/// by an authenticated HTTP client.
/// </summary>
/// <remarks>
/// Tenant and subject identifiers are deliberately
/// excluded from the request body.
///
/// Identity must be resolved from trusted server-side
/// authentication context.
/// </remarks>
public sealed record DocumentSearchHttpRequest(
    string Query,
    int MaximumResults = 10);

/// <summary>
/// Represents an authorized document-search hit.
/// </summary>
/// <remarks>
/// Search implementations must populate this record
/// only after enforcing tenant and document-level
/// authorization for the requesting subject.
/// </remarks>
public sealed record AuthorizedDocumentSearchHit(
    Guid DocumentId,
    string TenantId,
    string DocumentVersion,
    string ChunkId,
    string CitationId,
    string Excerpt,
    double Score,
    string? DocumentTitle = null,
    string? SourceUri = null);

/// <summary>
/// Represents a document-search hit returned
/// to the HTTP client.
/// </summary>
public sealed record DocumentSearchHttpHit(
    Guid DocumentId,
    string DocumentVersion,
    string ChunkId,
    string CitationId,
    string Excerpt,
    double Score,
    string? DocumentTitle,
    string? SourceUri);

/// <summary>
/// Represents the document-search HTTP response.
/// </summary>
public sealed record DocumentSearchHttpResponse(
    Guid RequestId,
    string Query,
    int ResultCount,
    IReadOnlyList<DocumentSearchHttpHit> Results);

/// <summary>
/// Defines a search service that returns only documents
/// accessible to the authenticated caller.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Enforce tenant isolation.
/// - Enforce document-level access permissions.
/// - Prevent cross-tenant search result disclosure.
/// - Respect the maximum result count.
/// - Return excerpts from authorized document versions.
/// - Avoid returning secrets or restricted document data
///   to callers without appropriate access.
///
/// This interface does not itself enforce authorization.
/// </remarks>
public interface IAuthorizedDocumentSearchService
{
    Task<IReadOnlyList<AuthorizedDocumentSearchHit>> SearchAsync(
        GroundedQuestionRequestIdentity identity,
        string query,
        int maximumResults,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines HTTP endpoints for authorized document search.
/// </summary>
public static class DocumentSearchEndpoints
{
    private const int MaximumQueryCharacters = 4000;
    private const int MaximumAllowedResults = 50;
    private const int MaximumExcerptCharacters = 4000;
    private const int MaximumMetadataCharacters = 2048;

    /// <summary>
    /// Registers the document-search endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapDocumentSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
                "/api/documents/search",
                HandleSearchAsync)
            .WithName("SearchDocuments")
            .WithTags("Documents")
            .Produces<DocumentSearchHttpResponse>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status400BadRequest)
            .Produces(
                StatusCodes.Status401Unauthorized)
            .Produces(
                StatusCodes.Status403Forbidden)
            .Produces(
                StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> HandleSearchAsync(
        DocumentSearchHttpRequest request,
        HttpContext httpContext,
        IGroundedQuestionRequestIdentityProvider identityProvider,
        IAuthorizedDocumentSearchService searchService,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Query) ||
            request.Query.Length > MaximumQueryCharacters ||
            request.MaximumResults <= 0 ||
            request.MaximumResults > MaximumAllowedResults)
        {
            return Results.BadRequest(
                new
                {
                    error = "Invalid document-search request."
                });
        }

        var identity = await identityProvider.GetIdentityAsync(
            httpContext,
            cancellationToken);

        if (identity is null ||
            string.IsNullOrWhiteSpace(identity.TenantId) ||
            string.IsNullOrWhiteSpace(identity.SubjectId))
        {
            return Results.Unauthorized();
        }

        IReadOnlyList<AuthorizedDocumentSearchHit> hits;

        try
        {
            hits = await searchService.SearchAsync(
                identity,
                request.Query,
                request.MaximumResults,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        if (hits is null ||
            hits.Count > request.MaximumResults)
        {
            return Results.Problem(
                "Document search returned an invalid result collection.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var responseHits =
            new List<DocumentSearchHttpHit>(hits.Count);

        var citationIds =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (var hit in hits)
        {
            if (!IsValidHit(hit, identity.TenantId))
            {
                return Results.Problem(
                    "Document search returned an invalid or unauthorized result.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!citationIds.Add(hit.CitationId))
            {
                return Results.Problem(
                    "Document search returned duplicate citation identifiers.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            responseHits.Add(
                new DocumentSearchHttpHit(
                    hit.DocumentId,
                    hit.DocumentVersion,
                    hit.ChunkId,
                    hit.CitationId,
                    hit.Excerpt,
                    hit.Score,
                    hit.DocumentTitle,
                    hit.SourceUri));
        }

        var response = new DocumentSearchHttpResponse(
            Guid.NewGuid(),
            request.Query,
            responseHits.Count,
            responseHits.ToArray());

        return Results.Ok(response);
    }

    /// <summary>
    /// Performs defensive validation of search results
    /// before exposing them through the API.
    /// </summary>
    private static bool IsValidHit(
        AuthorizedDocumentSearchHit? hit,
        string expectedTenantId)
    {
        if (hit is null ||
            hit.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(hit.TenantId) ||
            string.IsNullOrWhiteSpace(hit.DocumentVersion) ||
            string.IsNullOrWhiteSpace(hit.ChunkId) ||
            string.IsNullOrWhiteSpace(hit.CitationId) ||
            string.IsNullOrWhiteSpace(hit.Excerpt))
        {
            return false;
        }

        if (!string.Equals(
                hit.TenantId,
                expectedTenantId,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (hit.Excerpt.Length > MaximumExcerptCharacters)
        {
            return false;
        }

        if (hit.DocumentTitle is not null &&
            hit.DocumentTitle.Length > MaximumMetadataCharacters)
        {
            return false;
        }

        if (hit.SourceUri is not null &&
            hit.SourceUri.Length > MaximumMetadataCharacters)
        {
            return false;
        }

        if (double.IsNaN(hit.Score) ||
            double.IsInfinity(hit.Score))
        {
            return false;
        }

        return true;
    }
}