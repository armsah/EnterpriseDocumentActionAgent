namespace EnterpriseDocumentActionAgent.Application.Grounding;

/// <summary>
/// Represents an application-level request to compose
/// a grounded answer from authorized document evidence.
/// </summary>
/// <remarks>
/// Evidence must already have passed tenant and
/// document-access authorization checks before
/// this request is constructed.
/// </remarks>
public sealed record ComposeGroundedAnswerRequest(
    Guid RequestId,
    string TenantId,
    string Question,
    IReadOnlyList<GroundedAnswerEvidence> Evidence,
    GroundedAnswerGenerationOptions? Options = null);

/// <summary>
/// Represents a grounded-answer response prepared
/// for consumption by the application layer.
/// </summary>
public sealed record ComposeGroundedAnswerResult(
    Guid RequestId,
    string TenantId,
    GroundedAnswerGenerationOutcome Outcome,
    string? Answer,
    IReadOnlyList<GroundedAnswerCitation> Citations,
    string Provider,
    string Model,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Defines the application-facing grounded-answer
/// composition service.
/// </summary>
public interface IGroundedAnswerComposer
{
    /// <summary>
    /// Composes a grounded answer from authorized evidence.
    /// </summary>
    Task<ComposeGroundedAnswerResult> ComposeAsync(
        ComposeGroundedAnswerRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Coordinates grounded-answer generation and validates
/// structural consistency of the returned response.
/// </summary>
/// <remarks>
/// This service does not retrieve documents or enforce
/// document-level access permissions.
///
/// It validates tenant consistency and citation identity,
/// but does not independently establish that every
/// generated claim is semantically supported.
///
/// Semantic verification is a separate application
/// responsibility.
/// </remarks>
public sealed class GroundedAnswerComposer
    : IGroundedAnswerComposer
{
    private readonly IGroundedAnswerGenerator _generator;

    public GroundedAnswerComposer(
        IGroundedAnswerGenerator generator)
    {
        _generator = generator
            ?? throw new ArgumentNullException(
                nameof(generator));
    }

    /// <inheritdoc />
    public async Task<ComposeGroundedAnswerResult> ComposeAsync(
        ComposeGroundedAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequest(request);

        var generationRequest =
            new GenerateGroundedAnswerRequest(
                request.RequestId,
                request.TenantId,
                request.Question,
                request.Evidence,
                request.Options);

        var generationResult =
            await _generator.GenerateAsync(
                generationRequest,
                cancellationToken);

        if (generationResult is null)
        {
            throw new InvalidOperationException(
                "Grounded-answer generator returned no result.");
        }

        ValidateGenerationResult(
            request,
            generationResult);

        return new ComposeGroundedAnswerResult(
            generationResult.RequestId,
            generationResult.TenantId,
            generationResult.Outcome,
            generationResult.Answer,
            generationResult.Citations.ToArray(),
            generationResult.Provider,
            generationResult.Model,
            generationResult.GeneratedAtUtc);
    }

    /// <summary>
    /// Validates the incoming composition request.
    /// </summary>
    private static void ValidateRequest(
        ComposeGroundedAnswerRequest request)
    {
        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request ID cannot be empty.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            throw new ArgumentException(
                "Tenant ID is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException(
                "Question is required.",
                nameof(request));
        }

        if (request.Evidence is null)
        {
            throw new ArgumentException(
                "Evidence collection cannot be null.",
                nameof(request));
        }

        var citationIds = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var evidence in request.Evidence)
        {
            if (evidence is null ||
                evidence.DocumentId == Guid.Empty ||
                string.IsNullOrWhiteSpace(evidence.CitationId) ||
                string.IsNullOrWhiteSpace(evidence.ChunkId) ||
                string.IsNullOrWhiteSpace(evidence.DocumentVersion) ||
                string.IsNullOrWhiteSpace(evidence.Content))
            {
                throw new ArgumentException(
                    "Evidence contains an invalid item.",
                    nameof(request));
            }

            if (!string.Equals(
                    evidence.TenantId,
                    request.TenantId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Evidence tenant does not match request tenant.",
                    nameof(request));
            }

            if (!citationIds.Add(evidence.CitationId))
            {
                throw new ArgumentException(
                    "Evidence citation identifiers must be unique.",
                    nameof(request));
            }
        }

        if (request.Options is { } options)
        {
            if (options.MaximumAnswerCharacters <= 0 ||
                options.MaximumEvidenceItems <= 0 ||
                options.MaximumEvidenceCharacters <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "Generation limits must be positive.");
            }

            if (double.IsNaN(options.Temperature) ||
                double.IsInfinity(options.Temperature) ||
                options.Temperature < 0 ||
                options.Temperature > 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    "Temperature must be between zero and two.");
            }

            if (request.Evidence.Count >
                options.MaximumEvidenceItems)
            {
                throw new ArgumentException(
                    "Evidence count exceeds the configured limit.",
                    nameof(request));
            }

            var evidenceLength = request.Evidence.Sum(
                item => (long)item.Content.Length);

            if (evidenceLength >
                options.MaximumEvidenceCharacters)
            {
                throw new ArgumentException(
                    "Evidence content exceeds the configured limit.",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Validates the identity, outcome, and citation
    /// structure of a generated answer.
    /// </summary>
    private static void ValidateGenerationResult(
        ComposeGroundedAnswerRequest request,
        GenerateGroundedAnswerResult result)
    {
        if (result.RequestId != request.RequestId)
        {
            throw new InvalidOperationException(
                "Generator result request ID does not match.");
        }

        if (!string.Equals(
                result.TenantId,
                request.TenantId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Generator result tenant does not match.");
        }

        if (string.IsNullOrWhiteSpace(result.Provider) ||
            string.IsNullOrWhiteSpace(result.Model))
        {
            throw new InvalidOperationException(
                "Generator result is missing provider metadata.");
        }

        if (result.GeneratedAtUtc == default)
        {
            throw new InvalidOperationException(
                "Generator result has no generation timestamp.");
        }

        if (result.Citations is null)
        {
            throw new InvalidOperationException(
                "Generator result has no citation collection.");
        }

        var evidenceByCitationId =
            request.Evidence.ToDictionary(
                item => item.CitationId,
                StringComparer.Ordinal);

        var seenCitations = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var citation in result.Citations)
        {
            if (citation is null ||
                string.IsNullOrWhiteSpace(citation.CitationId) ||
                citation.DocumentId == Guid.Empty ||
                string.IsNullOrWhiteSpace(citation.ChunkId))
            {
                throw new InvalidOperationException(
                    "Generator returned an invalid citation.");
            }

            if (!seenCitations.Add(citation.CitationId))
            {
                throw new InvalidOperationException(
                    "Generator returned duplicate citations.");
            }

            if (!evidenceByCitationId.TryGetValue(
                    citation.CitationId,
                    out var evidence))
            {
                throw new InvalidOperationException(
                    "Generator referenced unknown evidence.");
            }

            if (citation.DocumentId != evidence.DocumentId ||
                !string.Equals(
                    citation.ChunkId,
                    evidence.ChunkId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Generator citation metadata does not match evidence.");
            }
        }

        switch (result.Outcome)
        {
            case GroundedAnswerGenerationOutcome.AnswerGenerated:
                if (string.IsNullOrWhiteSpace(result.Answer))
                {
                    throw new InvalidOperationException(
                        "Generated-answer outcome requires answer text.");
                }

                if (result.Citations.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Generated answers require citations.");
                }

                break;

            case GroundedAnswerGenerationOutcome.InsufficientEvidence:
            case GroundedAnswerGenerationOutcome.Rejected:
                if (!string.IsNullOrWhiteSpace(result.Answer) ||
                    result.Citations.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Non-answer outcomes cannot contain an answer or citations.");
                }

                break;

            default:
                throw new InvalidOperationException(
                    "Generator returned an unknown outcome.");
        }

        var maximumAnswerCharacters =
            (request.Options ??
             new GroundedAnswerGenerationOptions())
            .MaximumAnswerCharacters;

        if (result.Answer is not null &&
            result.Answer.Length > maximumAnswerCharacters)
        {
            throw new InvalidOperationException(
                "Generated answer exceeds the configured length limit.");
        }
    }
}