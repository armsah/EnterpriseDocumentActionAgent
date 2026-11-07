namespace EnterpriseDocumentActionAgent.Application.Grounding;

/// <summary>
/// Represents the result of deterministic grounded-answer
/// verification.
/// </summary>
public enum GroundedAnswerVerificationStatus
{
    /// <summary>
    /// The generated answer failed structural or
    /// citation validation.
    /// </summary>
    Rejected = 1,

    /// <summary>
    /// The answer passed deterministic checks,
    /// but semantic claim support has not been established.
    /// </summary>
    RequiresSemanticReview = 2,

    /// <summary>
    /// A valid non-answer outcome was returned.
    /// No factual answer requires verification.
    /// </summary>
    NoAnswer = 3
}

/// <summary>
/// Identifies a deterministic verification failure.
/// </summary>
public enum GroundedAnswerVerificationIssueCode
{
    InvalidRequest = 1,
    InvalidResult = 2,
    RequestIdMismatch = 3,
    TenantMismatch = 4,
    MissingAnswer = 5,
    UnexpectedAnswer = 6,
    MissingCitations = 7,
    UnexpectedCitations = 8,
    InvalidCitation = 9,
    UnknownCitation = 10,
    DuplicateCitation = 11,
    CitationMetadataMismatch = 12,
    InvalidOutcome = 13,
    AnswerTooLong = 14
}

/// <summary>
/// Describes an issue detected during verification.
/// </summary>
public sealed record GroundedAnswerVerificationIssue(
    GroundedAnswerVerificationIssueCode Code,
    string Message,
    string? CitationId = null);

/// <summary>
/// Represents a deterministic verification result.
/// </summary>
/// <remarks>
/// RequiresSemanticReview means only that structural
/// and citation checks passed.
///
/// It does not mean the generated answer is accurate,
/// complete, or supported by the cited evidence.
/// </remarks>
public sealed record GroundedAnswerVerificationResult(
    Guid RequestId,
    string TenantId,
    GroundedAnswerVerificationStatus Status,
    IReadOnlyList<GroundedAnswerVerificationIssue> Issues);

/// <summary>
/// Defines the application-facing grounded-answer
/// verification service.
/// </summary>
public interface IGroundedAnswerVerifier
{
    /// <summary>
    /// Verifies a generated answer against the
    /// original request and supplied evidence.
    /// </summary>
    GroundedAnswerVerificationResult Verify(
        ComposeGroundedAnswerRequest request,
        ComposeGroundedAnswerResult result);
}

/// <summary>
/// Performs deterministic validation of grounded-answer
/// structure, tenant identity, and citation integrity.
/// </summary>
/// <remarks>
/// This verifier does not perform semantic entailment,
/// fact-checking, or independent source authentication.
///
/// A result with RequiresSemanticReview must not be
/// represented to users as a factually verified answer.
/// </remarks>
public sealed class GroundedAnswerVerifier
    : IGroundedAnswerVerifier
{
    /// <inheritdoc />
    public GroundedAnswerVerificationResult Verify(
        ComposeGroundedAnswerRequest request,
        ComposeGroundedAnswerResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var issues =
            new List<GroundedAnswerVerificationIssue>();

        ValidateRequest(request, issues);
        ValidateResult(request, result, issues);

        if (issues.Count > 0)
        {
            return new GroundedAnswerVerificationResult(
                request.RequestId,
                request.TenantId,
                GroundedAnswerVerificationStatus.Rejected,
                issues.AsReadOnly());
        }

        var status =
            result.Outcome ==
            GroundedAnswerGenerationOutcome.AnswerGenerated
                ? GroundedAnswerVerificationStatus
                    .RequiresSemanticReview
                : GroundedAnswerVerificationStatus.NoAnswer;

        return new GroundedAnswerVerificationResult(
            request.RequestId,
            request.TenantId,
            status,
            Array.Empty<GroundedAnswerVerificationIssue>());
    }

    private static void ValidateRequest(
        ComposeGroundedAnswerRequest request,
        List<GroundedAnswerVerificationIssue> issues)
    {
        if (request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.TenantId) ||
            string.IsNullOrWhiteSpace(request.Question) ||
            request.Evidence is null)
        {
            AddIssue(
                issues,
                GroundedAnswerVerificationIssueCode.InvalidRequest,
                "The grounded-answer request is invalid.");

            return;
        }

        var seenIds = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var evidence in request.Evidence)
        {
            if (evidence is null ||
                evidence.DocumentId == Guid.Empty ||
                string.IsNullOrWhiteSpace(evidence.CitationId) ||
                string.IsNullOrWhiteSpace(evidence.ChunkId) ||
                string.IsNullOrWhiteSpace(evidence.DocumentVersion) ||
                string.IsNullOrWhiteSpace(evidence.Content) ||
                !string.Equals(
                    evidence.TenantId,
                    request.TenantId,
                    StringComparison.Ordinal) ||
                !seenIds.Add(evidence.CitationId))
            {
                AddIssue(
                    issues,
                    GroundedAnswerVerificationIssueCode.InvalidRequest,
                    "The request contains invalid or duplicate evidence.");

                return;
            }
        }

        var options =
            request.Options ?? new GroundedAnswerGenerationOptions();

        if (options.MaximumAnswerCharacters <= 0 ||
            options.MaximumEvidenceItems <= 0 ||
            options.MaximumEvidenceCharacters <= 0 ||
            double.IsNaN(options.Temperature) ||
            double.IsInfinity(options.Temperature) ||
            options.Temperature < 0 ||
            options.Temperature > 2)
        {
            AddIssue(
                issues,
                GroundedAnswerVerificationIssueCode.InvalidRequest,
                "The generation options are invalid.");

            return;
        }

        if (request.Evidence.Count > options.MaximumEvidenceItems ||
            request.Evidence.Sum(
                item => (long)item.Content.Length) >
            options.MaximumEvidenceCharacters)
        {
            AddIssue(
                issues,
                GroundedAnswerVerificationIssueCode.InvalidRequest,
                "The request exceeds its evidence limits.");
        }
    }

    private static void ValidateResult(
        ComposeGroundedAnswerRequest request,
        ComposeGroundedAnswerResult result,
        List<GroundedAnswerVerificationIssue> issues)
    {
        if (result.RequestId != request.RequestId)
        {
            AddIssue(
                issues,
                GroundedAnswerVerificationIssueCode.RequestIdMismatch,
                "The result request identifier does not match.");
        }

        if (!string.Equals(
                result.TenantId,
                request.TenantId,
                StringComparison.Ordinal))
        {
            AddIssue(
                issues,
                GroundedAnswerVerificationIssueCode.TenantMismatch,
                "The result tenant does not match.");
        }

        if (string.IsNullOrWhiteSpace(result.Provider) ||
            string.IsNullOrWhiteSpace(result.Model) ||
            result.GeneratedAtUtc == default)
        {
            AddIssue(
                issues,
                GroundedAnswerVerificationIssueCode.InvalidResult,
                "The result provider metadata is invalid.");
        }

        if (result.Citations is null)
        {
            AddIssue(
                issues,
                GroundedAnswerVerificationIssueCode.InvalidResult,
                "The result citation collection is missing.");

            return;
        }

        var evidenceById =
            new Dictionary<string, GroundedAnswerEvidence>(
                StringComparer.Ordinal);

        if (request.Evidence is not null)
        {
            foreach (var evidence in request.Evidence)
            {
                if (evidence is not null &&
                    !string.IsNullOrWhiteSpace(evidence.CitationId))
                {
                    evidenceById.TryAdd(
                        evidence.CitationId,
                        evidence);
                }
            }
        }

        var seenCitations = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var citation in result.Citations)
        {
            if (citation is null ||
                string.IsNullOrWhiteSpace(citation.CitationId) ||
                citation.DocumentId == Guid.Empty ||
                string.IsNullOrWhiteSpace(citation.ChunkId))
            {
                AddIssue(
                    issues,
                    GroundedAnswerVerificationIssueCode.InvalidCitation,
                    "The result contains an invalid citation.");

                continue;
            }

            if (!seenCitations.Add(citation.CitationId))
            {
                AddIssue(
                    issues,
                    GroundedAnswerVerificationIssueCode.DuplicateCitation,
                    "The result contains a duplicate citation.",
                    citation.CitationId);

                continue;
            }

            if (!evidenceById.TryGetValue(
                    citation.CitationId,
                    out var evidence))
            {
                AddIssue(
                    issues,
                    GroundedAnswerVerificationIssueCode.UnknownCitation,
                    "The citation does not reference supplied evidence.",
                    citation.CitationId);

                continue;
            }

            if (citation.DocumentId != evidence.DocumentId ||
                !string.Equals(
                    citation.ChunkId,
                    evidence.ChunkId,
                    StringComparison.Ordinal))
            {
                AddIssue(
                    issues,
                    GroundedAnswerVerificationIssueCode
                        .CitationMetadataMismatch,
                    "The citation metadata does not match the evidence.",
                    citation.CitationId);
            }
        }

        var maximumAnswerCharacters =
            (request.Options ??
             new GroundedAnswerGenerationOptions())
            .MaximumAnswerCharacters;

        if (result.Answer is not null &&
            result.Answer.Length > maximumAnswerCharacters)
        {
            AddIssue(
                issues,
                GroundedAnswerVerificationIssueCode.AnswerTooLong,
                "The generated answer exceeds the configured limit.");
        }

        switch (result.Outcome)
        {
            case GroundedAnswerGenerationOutcome.AnswerGenerated:
                if (string.IsNullOrWhiteSpace(result.Answer))
                {
                    AddIssue(
                        issues,
                        GroundedAnswerVerificationIssueCode.MissingAnswer,
                        "A generated-answer result requires answer text.");
                }

                if (result.Citations.Count == 0)
                {
                    AddIssue(
                        issues,
                        GroundedAnswerVerificationIssueCode.MissingCitations,
                        "A generated-answer result requires citations.");
                }

                break;

            case GroundedAnswerGenerationOutcome.InsufficientEvidence:
            case GroundedAnswerGenerationOutcome.Rejected:
                if (!string.IsNullOrWhiteSpace(result.Answer))
                {
                    AddIssue(
                        issues,
                        GroundedAnswerVerificationIssueCode.UnexpectedAnswer,
                        "A non-answer result must not contain answer text.");
                }

                if (result.Citations.Count > 0)
                {
                    AddIssue(
                        issues,
                        GroundedAnswerVerificationIssueCode
                            .UnexpectedCitations,
                        "A non-answer result must not contain citations.");
                }

                break;

            default:
                AddIssue(
                    issues,
                    GroundedAnswerVerificationIssueCode.InvalidOutcome,
                    "The result contains an unsupported outcome.");

                break;
        }
    }

    private static void AddIssue(
        List<GroundedAnswerVerificationIssue> issues,
        GroundedAnswerVerificationIssueCode code,
        string message,
        string? citationId = null)
    {
        issues.Add(
            new GroundedAnswerVerificationIssue(
                code,
                message,
                citationId));
    }
}