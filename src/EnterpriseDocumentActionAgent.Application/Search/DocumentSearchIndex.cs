using System.Collections.ObjectModel;

namespace EnterpriseDocumentActionAgent.Application.Search;

/// <summary>
/// Defines the supported document search modes.
/// </summary>
public enum DocumentSearchMode
{
    Keyword = 1,
    Semantic = 2,
    Hybrid = 3
}

/// <summary>
/// Represents the trusted security context used
/// to filter document search results.
/// </summary>
/// <remarks>
/// TenantId, PrincipalId, and AllowedSecurityGroups
/// must originate from authenticated and authorized
/// application infrastructure.
///
/// Client-supplied security claims must not be trusted.
///
/// Search implementations must enforce tenant and
/// document ACL restrictions within the query itself.
/// </remarks>
public sealed record DocumentSearchSecurityContext(
    string TenantId,
    string PrincipalId,
    IReadOnlyList<string> AllowedSecurityGroups);

/// <summary>
/// Represents a request to search indexed documents.
/// </summary>
/// <remarks>
/// QueryText contains the user's search terms.
///
/// Semantic and hybrid search implementations may
/// generate a query embedding before executing
/// vector retrieval.
///
/// MaximumResults limits the number of returned hits.
///
/// ContinuationToken is an opaque pagination token.
/// Implementations must bind tokens to the tenant,
/// principal, query, and security filters.
/// </remarks>
public sealed record DocumentSearchRequest(
    string QueryText,
    DocumentSearchMode Mode,
    DocumentSearchSecurityContext SecurityContext,
    int MaximumResults = 10,
    string? ContinuationToken = null);

/// <summary>
/// Represents a citation for an indexed document passage.
/// </summary>
public sealed record DocumentSearchCitation(
    Guid DocumentId,
    string ChunkId,
    string? DocumentTitle,
    int? PageNumber,
    string? SectionHeading);

/// <summary>
/// Represents a single ranked document search result.
/// </summary>
/// <remarks>
/// Score is a normalized relevance score between
/// zero and one.
///
/// The score is not an authorization decision.
///
/// Snippet may contain sensitive document text
/// and must only be returned after ACL enforcement.
/// </remarks>
public sealed record DocumentSearchHit(
    Guid DocumentId,
    string TenantId,
    string ChunkId,
    string Snippet,
    double Score,
    DocumentSearchCitation Citation);

/// <summary>
/// Represents a page of document search results.
/// </summary>
public sealed record DocumentSearchResult(
    IReadOnlyList<DocumentSearchHit> Hits,
    string? ContinuationToken);

/// <summary>
/// Defines the application-facing document search index.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Enforce tenant isolation at query time.
/// - Enforce document ACL restrictions at query time.
/// - Reject invalid or untrusted security contexts.
/// - Prevent unauthorized snippets from being returned.
/// - Preserve citation-to-chunk relationships.
/// - Return results ordered by descending relevance.
/// - Bind continuation tokens to the security context.
/// - Avoid exposing internal database identifiers
///   through pagination tokens.
/// - Honor cancellation.
///
/// This interface does not authenticate callers.
/// Authentication must occur before creating the
/// trusted search security context.
/// </remarks>
public interface IDocumentSearchIndex
{
    /// <summary>
    /// Searches indexed documents under the
    /// supplied trusted security context.
    /// </summary>
    Task<DocumentSearchResult> SearchAsync(
        DocumentSearchRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Validates document search requests and results.
/// </summary>
/// <remarks>
/// These checks provide defense in depth.
///
/// They do not replace query-time tenant isolation,
/// ACL enforcement, or secure pagination.
/// </remarks>
public static class DocumentSearchValidation
{
    public const int MaximumQueryCharacters = 4000;
    public const int MaximumPageSize = 100;
    public const int MaximumIdentifierCharacters = 256;
    public const int MaximumSecurityGroups = 256;
    public const int MaximumContinuationTokenCharacters = 4096;

    /// <summary>
    /// Validates a document search request.
    /// </summary>
    public static void ValidateRequest(
        DocumentSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.QueryText);

        if (request.QueryText.Length > MaximumQueryCharacters)
        {
            throw new ArgumentException(
                "Search query exceeds the maximum length.",
                nameof(request));
        }

        if (request.QueryText.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Search query contains invalid control characters.",
                nameof(request));
        }

        if (!Enum.IsDefined(request.Mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Unsupported document search mode.");
        }

        if (request.MaximumResults < 1 ||
            request.MaximumResults > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Search result limit is outside the permitted range.");
        }

        ValidateSecurityContext(request.SecurityContext);

        if (request.ContinuationToken is not null)
        {
            if (string.IsNullOrWhiteSpace(
                    request.ContinuationToken))
            {
                throw new ArgumentException(
                    "Continuation token cannot be empty.",
                    nameof(request));
            }

            if (request.ContinuationToken.Length >
                MaximumContinuationTokenCharacters)
            {
                throw new ArgumentException(
                    "Continuation token exceeds the maximum length.",
                    nameof(request));
            }

            if (request.ContinuationToken.Any(char.IsControl))
            {
                throw new ArgumentException(
                    "Continuation token contains invalid characters.",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Validates the structure of a trusted
    /// document search security context.
    /// </summary>
    public static void ValidateSecurityContext(
        DocumentSearchSecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ValidateIdentifier(
            context.TenantId,
            nameof(context.TenantId));

        ValidateIdentifier(
            context.PrincipalId,
            nameof(context.PrincipalId));

        ArgumentNullException.ThrowIfNull(
            context.AllowedSecurityGroups);

        if (context.AllowedSecurityGroups.Count >
            MaximumSecurityGroups)
        {
            throw new ArgumentException(
                "Security group count exceeds the permitted limit.",
                nameof(context));
        }

        var groups = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var group in context.AllowedSecurityGroups)
        {
            ValidateIdentifier(
                group,
                "securityGroup");

            if (!groups.Add(group))
            {
                throw new ArgumentException(
                    "Security context contains duplicate groups.",
                    nameof(context));
            }
        }
    }

    /// <summary>
    /// Validates a document search result.
    /// </summary>
    /// <remarks>
    /// Tenant checks are defense in depth.
    /// Implementations must still enforce ACL
    /// restrictions before returning any snippets.
    /// </remarks>
    public static void ValidateResult(
        DocumentSearchRequest request,
        DocumentSearchResult result)
    {
        ValidateRequest(request);

        ArgumentNullException.ThrowIfNull(result);

        ArgumentNullException.ThrowIfNull(result.Hits);

        if (result.Hits.Count > request.MaximumResults)
        {
            throw new InvalidOperationException(
                "Search result exceeds the requested page size.");
        }

        double? previousScore = null;

        foreach (var hit in result.Hits)
        {
            if (hit is null)
            {
                throw new InvalidOperationException(
                    "Search result contains a null hit.");
            }

            if (hit.DocumentId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "Search result contains an invalid document ID.");
            }

            if (!string.Equals(
                    hit.TenantId,
                    request.SecurityContext.TenantId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Search result violates tenant isolation.");
            }

            if (string.IsNullOrWhiteSpace(hit.ChunkId))
            {
                throw new InvalidOperationException(
                    "Search result contains an invalid chunk ID.");
            }

            if (hit.Snippet is null)
            {
                throw new InvalidOperationException(
                    "Search result contains a null snippet.");
            }

            if (!double.IsFinite(hit.Score) ||
                hit.Score < 0d ||
                hit.Score > 1d)
            {
                throw new InvalidOperationException(
                    "Search result contains an invalid score.");
            }

            if (previousScore.HasValue &&
                hit.Score > previousScore.Value)
            {
                throw new InvalidOperationException(
                    "Search results are not ordered by relevance.");
            }

            ValidateCitation(hit);

            previousScore = hit.Score;
        }

        if (result.ContinuationToken is not null)
        {
            if (string.IsNullOrWhiteSpace(
                    result.ContinuationToken) ||
                result.ContinuationToken.Length >
                    MaximumContinuationTokenCharacters ||
                result.ContinuationToken.Any(char.IsControl))
            {
                throw new InvalidOperationException(
                    "Search result contains an invalid continuation token.");
            }
        }
    }

    /// <summary>
    /// Validates a citation against its search hit.
    /// </summary>
    private static void ValidateCitation(
        DocumentSearchHit hit)
    {
        if (hit.Citation is null)
        {
            throw new InvalidOperationException(
                "Search hit is missing citation information.");
        }

        if (hit.Citation.DocumentId != hit.DocumentId)
        {
            throw new InvalidOperationException(
                "Citation document identifier does not match its hit.");
        }

        if (!string.Equals(
                hit.Citation.ChunkId,
                hit.ChunkId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Citation chunk identifier does not match its hit.");
        }

        if (hit.Citation.PageNumber is <= 0)
        {
            throw new InvalidOperationException(
                "Citation page number must be positive.");
        }
    }

    private static void ValidateIdentifier(
        string value,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value,
            parameterName);

        if (value.Length > MaximumIdentifierCharacters)
        {
            throw new ArgumentException(
                "Search identifier exceeds the permitted length.",
                parameterName);
        }

        if (!string.Equals(
                value,
                value.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Search identifier must be normalized.",
                parameterName);
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Search identifier contains invalid control characters.",
                parameterName);
        }
    }
}