using EnterpriseDocumentActionAgent.Application.Grounding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EnterpriseDocumentActionAgent.Api;

/// <summary>
/// Represents a request to answer a question using
/// documents accessible to the current caller.
/// </summary>
public sealed record GroundedQuestionHttpRequest(
    string Question,
    int MaximumEvidenceItems = 10,
    int MaximumAnswerCharacters = 8000);

/// <summary>
/// Represents a citation included in an HTTP response.
/// </summary>
public sealed record GroundedQuestionHttpCitation(
    string CitationId,
    Guid DocumentId,
    string ChunkId);

/// <summary>
/// Represents the HTTP response for a grounded question.
/// </summary>
public sealed record GroundedQuestionHttpResponse(
    Guid RequestId,
    string Outcome,
    string? Answer,
    IReadOnlyList<GroundedQuestionHttpCitation> Citations,
    string? Provider,
    string? Model,
    string VerificationStatus);

/// <summary>
/// Represents a trusted request identity.
/// </summary>
/// <remarks>
/// Implementations must derive identity from authenticated
/// server-side context rather than client-supplied tenant IDs.
/// </remarks>
public sealed record GroundedQuestionRequestIdentity(
    string TenantId,
    string SubjectId);

/// <summary>
/// Resolves the authenticated identity for a request.
/// </summary>
public interface IGroundedQuestionRequestIdentityProvider
{
    Task<GroundedQuestionRequestIdentity?> GetIdentityAsync(
        HttpContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Retrieves evidence that the authenticated caller
/// is authorized to access.
/// </summary>
/// <remarks>
/// Implementations must enforce both tenant isolation
/// and document-level authorization.
/// </remarks>
public interface IAuthorizedGroundedEvidenceRetriever
{
    Task<IReadOnlyList<GroundedAnswerEvidence>> RetrieveAsync(
        GroundedQuestionRequestIdentity identity,
        string question,
        int maximumItems,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines the HTTP endpoints for grounded
/// document question answering.
/// </summary>
public static class GroundedQuestionEndpoints
{
    private const int MaximumQuestionCharacters = 4000;
    private const int MaximumEvidenceItems = 20;
    private const int MaximumAnswerCharacters = 16000;

    /// <summary>
    /// Registers the grounded-question endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapGroundedQuestionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(
                "/api/grounded-questions",
                HandleQuestionAsync)
            .WithName("AskGroundedQuestion")
            .WithTags("Grounding")
            .Produces<GroundedQuestionHttpResponse>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status400BadRequest)
            .Produces(
                StatusCodes.Status401Unauthorized)
            .Produces(
                StatusCodes.Status403Forbidden)
            .Produces(
                StatusCodes.Status422UnprocessableEntity)
            .Produces(
                StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> HandleQuestionAsync(
        GroundedQuestionHttpRequest request,
        HttpContext httpContext,
        IGroundedQuestionRequestIdentityProvider identityProvider,
        IAuthorizedGroundedEvidenceRetriever evidenceRetriever,
        IGroundedAnswerComposer composer,
        IGroundedAnswerVerifier verifier,
        IGroundedAnswerValidation validator,
        IPromptInjectionDefense promptInjectionDefense,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.Question) ||
            request.Question.Length > MaximumQuestionCharacters ||
            request.MaximumEvidenceItems <= 0 ||
            request.MaximumEvidenceItems > MaximumEvidenceItems ||
            request.MaximumAnswerCharacters <= 0 ||
            request.MaximumAnswerCharacters > MaximumAnswerCharacters)
        {
            return Results.BadRequest(
                new
                {
                    error = "Invalid grounded-question request."
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

        IReadOnlyList<GroundedAnswerEvidence> evidence;

        try
        {
            evidence = await evidenceRetriever.RetrieveAsync(
                identity,
                request.Question,
                request.MaximumEvidenceItems,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Forbid();
        }

        if (evidence is null)
        {
            return Results.Problem(
                "Evidence retrieval returned no collection.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (evidence.Count > request.MaximumEvidenceItems)
        {
            return Results.Problem(
                "Evidence retrieval exceeded the requested item limit.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var screening =
            promptInjectionDefense.Screen(evidence);

        if (!screening.MayContinue)
        {
            return Results.UnprocessableEntity(
                new
                {
                    error =
                        "Retrieved evidence did not pass security screening."
                });
        }

        var options = new GroundedAnswerGenerationOptions(
            MaximumAnswerCharacters:
                request.MaximumAnswerCharacters,
            MaximumEvidenceItems:
                request.MaximumEvidenceItems);

        var composeRequest = new ComposeGroundedAnswerRequest(
            Guid.NewGuid(),
            identity.TenantId,
            request.Question,
            screening.AcceptedEvidence,
            options);

        var requestValidation =
            validator.ValidateRequest(composeRequest);

        if (!requestValidation.IsValid)
        {
            return Results.UnprocessableEntity(
                new
                {
                    error = "Retrieved evidence failed validation."
                });
        }

        var result = await composer.ComposeAsync(
            composeRequest,
            cancellationToken);

        var resultValidation =
            validator.ValidateResult(
                composeRequest,
                result);

        if (!resultValidation.IsValid)
        {
            return Results.Problem(
                "Generated response failed structural validation.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var verification = verifier.Verify(
            composeRequest,
            result);

        if (verification.Status ==
            GroundedAnswerVerificationStatus.Rejected)
        {
            return Results.Problem(
                "Generated response failed citation verification.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        // Deterministic citation checks cannot prove that
        // generated claims are semantically supported.
        //
        // Do not return an unverified generated answer
        // as a trusted, evidence-grounded response.
        if (verification.Status ==
            GroundedAnswerVerificationStatus.RequiresSemanticReview)
        {
            return Results.UnprocessableEntity(
                new
                {
                    error =
                        "The generated answer requires semantic verification.",
                    requestId = result.RequestId
                });
        }

        var response = new GroundedQuestionHttpResponse(
            result.RequestId,
            result.Outcome.ToString(),
            result.Answer,
            result.Citations.Select(
                citation => new GroundedQuestionHttpCitation(
                    citation.CitationId,
                    citation.DocumentId,
                    citation.ChunkId))
                .ToArray(),
            result.Provider,
            result.Model,
            verification.Status.ToString());

        return Results.Ok(response);
    }
}