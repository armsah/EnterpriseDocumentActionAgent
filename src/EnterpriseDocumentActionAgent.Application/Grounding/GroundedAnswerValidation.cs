namespace EnterpriseDocumentActionAgent.Application.Grounding;

/// <summary>
/// Identifies a validation issue affecting a grounded-answer
/// request or generated response.
/// </summary>
public enum GroundedAnswerValidationIssueCode
{
    InvalidRequestId = 1,
    InvalidTenantId = 2,
    InvalidQuestion = 3,
    MissingEvidenceCollection = 4,
    InvalidEvidence = 5,
    EvidenceTenantMismatch = 6,
    DuplicateEvidenceCitationId = 7,
    TooManyEvidenceItems = 8,
    EvidenceTooLong = 9,
    InvalidGenerationOptions = 10,
    ResultRequestIdMismatch = 11,
    ResultTenantMismatch = 12,
    InvalidProviderMetadata = 13,
    MissingResultCitations = 14,
    InvalidResultCitation = 15,
    UnknownResultCitation = 16,
    DuplicateResultCitation = 17,
    CitationMetadataMismatch = 18,
    MissingAnswer = 19,
    UnexpectedAnswer = 20,
    MissingAnswerCitations = 21,
    UnexpectedAnswerCitations = 22,
    InvalidGenerationOutcome = 23,
    AnswerTooLong = 24
}

/// <summary>
/// Describes an individual grounded-answer validation issue.
/// </summary>
public sealed record GroundedAnswerValidationIssue(
    GroundedAnswerValidationIssueCode Code,
    string Message,
    string? CitationId = null);

/// <summary>
/// Represents the outcome of grounded-answer validation.
/// </summary>
/// <remarks>
/// IsValid means that deterministic structural checks passed.
///
/// It does not establish that a generated answer is factually
/// correct or semantically supported by cited evidence.
/// </remarks>
public sealed record GroundedAnswerValidationResult(
    IReadOnlyList<GroundedAnswerValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    /// <summary>
    /// Throws when one or more validation issues exist.
    /// </summary>
    public void ThrowIfInvalid()
    {
        if (IsValid)
        {
            return;
        }

        throw new GroundedAnswerValidationException(Issues);
    }
}

/// <summary>
/// Represents a deterministic grounded-answer
/// validation failure.
/// </summary>
public sealed class GroundedAnswerValidationException : Exception
{
    public IReadOnlyList<GroundedAnswerValidationIssue> Issues
    {
        get;
    }

    public GroundedAnswerValidationException(
        IReadOnlyList<GroundedAnswerValidationIssue> issues)
        : base("Grounded-answer validation failed.")
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (issues.Count == 0)
        {
            throw new ArgumentException(
                "Validation exceptions require at least one issue.",
                nameof(issues));
        }

        Issues = issues.ToArray();
    }
}

/// <summary>
/// Defines reusable validation operations for grounded-answer
/// requests and generated responses.
/// </summary>
public interface IGroundedAnswerValidation
{
    GroundedAnswerValidationResult ValidateRequest(
        ComposeGroundedAnswerRequest request);

    GroundedAnswerValidationResult ValidateResult(
        ComposeGroundedAnswerRequest request,
        ComposeGroundedAnswerResult result);
}

/// <summary>
/// Provides deterministic validation of grounded-answer
/// requests, evidence, and generated responses.
/// </summary>
/// <remarks>
/// This component validates structural integrity only.
///
/// It does not:
///
/// - Authenticate the caller.
/// - Authorize access to source documents.
/// - Perform semantic entailment.
/// - Prove that citations support generated claims.
/// - Execute instructions contained in retrieved evidence.
/// </remarks>
public sealed class GroundedAnswerValidation
    : IGroundedAnswerValidation
{
    /// <inheritdoc />
    public GroundedAnswerValidationResult ValidateRequest(
        ComposeGroundedAnswerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues =
            new List<GroundedAnswerValidationIssue>();

        ValidateRequestCore(request, issues);

        return new GroundedAnswerValidationResult(
            issues.ToArray());
    }

    /// <inheritdoc />
    public GroundedAnswerValidationResult ValidateResult(
        ComposeGroundedAnswerRequest request,
        ComposeGroundedAnswerResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        var issues =
            new List<GroundedAnswerValidationIssue>();

        ValidateRequestCore(request, issues);
        ValidateResultCore(request, result, issues);

        return new GroundedAnswerValidationResult(
            issues.ToArray());
    }

    private static void ValidateRequestCore(
        ComposeGroundedAnswerRequest request,
        List<GroundedAnswerValidationIssue> issues)
    {
        if (request.RequestId == Guid.Empty)
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode.InvalidRequestId,
                "Request ID must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode.InvalidTenantId,
                "Tenant ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode.InvalidQuestion,
                "Question is required.");
        }

        if (request.Evidence is null)
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode
                    .MissingEvidenceCollection,
                "Evidence collection is required.");

            return;
        }

        var seenCitationIds =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (var evidence in request.Evidence)
        {
            if (evidence is null ||
                evidence.DocumentId == Guid.Empty ||
                string.IsNullOrWhiteSpace(evidence.CitationId) ||
                string.IsNullOrWhiteSpace(evidence.ChunkId) ||
                string.IsNullOrWhiteSpace(evidence.DocumentVersion) ||
                string.IsNullOrWhiteSpace(evidence.Content))
            {
                AddIssue(
                    issues,
                    GroundedAnswerValidationIssueCode.InvalidEvidence,
                    "Evidence contains an invalid item.");

                continue;
            }

            if (!string.Equals(
                    evidence.TenantId,
                    request.TenantId,
                    StringComparison.Ordinal))
            {
                AddIssue(
                    issues,
                    GroundedAnswerValidationIssueCode
                        .EvidenceTenantMismatch,
                    "Evidence tenant does not match the request tenant.",
                    evidence.CitationId);
            }

            if (!seenCitationIds.Add(evidence.CitationId))
            {
                AddIssue(
                    issues,
                    GroundedAnswerValidationIssueCode
                        .DuplicateEvidenceCitationId,
                    "Evidence citation identifiers must be unique.",
                    evidence.CitationId);
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
                GroundedAnswerValidationIssueCode
                    .InvalidGenerationOptions,
                "Generation options are invalid.");

            return;
        }

        if (request.Evidence.Count > options.MaximumEvidenceItems)
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode
                    .TooManyEvidenceItems,
                "Evidence item count exceeds the configured limit.");
        }

        var totalEvidenceCharacters = 0L;

        foreach (var evidence in request.Evidence)
        {
            if (evidence is not null &&
                evidence.Content is not null)
            {
                totalEvidenceCharacters += evidence.Content.Length;
            }
        }

        if (totalEvidenceCharacters >
            options.MaximumEvidenceCharacters)
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode.EvidenceTooLong,
                "Evidence content exceeds the configured limit.");
        }
    }

    private static void ValidateResultCore(
        ComposeGroundedAnswerRequest request,
        ComposeGroundedAnswerResult result,
        List<GroundedAnswerValidationIssue> issues)
    {
        if (result.RequestId != request.RequestId)
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode
                    .ResultRequestIdMismatch,
                "Result request ID does not match.");
        }

        if (!string.Equals(
                result.TenantId,
                request.TenantId,
                StringComparison.Ordinal))
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode
                    .ResultTenantMismatch,
                "Result tenant does not match.");
        }

        if (string.IsNullOrWhiteSpace(result.Provider) ||
            string.IsNullOrWhiteSpace(result.Model) ||
            result.GeneratedAtUtc == default)
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode
                    .InvalidProviderMetadata,
                "Result provider metadata is invalid.");
        }

        if (result.Citations is null)
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode
                    .MissingResultCitations,
                "Result citation collection is required.");

            return;
        }

        var evidenceByCitationId =
            new Dictionary<string, GroundedAnswerEvidence>(
                StringComparer.Ordinal);

        if (request.Evidence is not null)
        {
            foreach (var evidence in request.Evidence)
            {
                if (evidence is not null &&
                    !string.IsNullOrWhiteSpace(evidence.CitationId))
                {
                    evidenceByCitationId.TryAdd(
                        evidence.CitationId,
                        evidence);
                }
            }
        }

        var seenResultCitations =
            new HashSet<string>(StringComparer.Ordinal);

        foreach (var citation in result.Citations)
        {
            if (citation is null ||
                string.IsNullOrWhiteSpace(citation.CitationId) ||
                citation.DocumentId == Guid.Empty ||
                string.IsNullOrWhiteSpace(citation.ChunkId))
            {
                AddIssue(
                    issues,
                    GroundedAnswerValidationIssueCode
                        .InvalidResultCitation,
                    "Result contains an invalid citation.");

                continue;
            }

            if (!seenResultCitations.Add(citation.CitationId))
            {
                AddIssue(
                    issues,
                    GroundedAnswerValidationIssueCode
                        .DuplicateResultCitation,
                    "Result contains a duplicate citation.",
                    citation.CitationId);

                continue;
            }

            if (!evidenceByCitationId.TryGetValue(
                    citation.CitationId,
                    out var evidence))
            {
                AddIssue(
                    issues,
                    GroundedAnswerValidationIssueCode
                        .UnknownResultCitation,
                    "Result references unknown evidence.",
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
                    GroundedAnswerValidationIssueCode
                        .CitationMetadataMismatch,
                    "Citation metadata does not match evidence.",
                    citation.CitationId);
            }
        }

        var options =
            request.Options ?? new GroundedAnswerGenerationOptions();

        if (options.MaximumAnswerCharacters > 0 &&
            result.Answer is not null &&
            result.Answer.Length > options.MaximumAnswerCharacters)
        {
            AddIssue(
                issues,
                GroundedAnswerValidationIssueCode.AnswerTooLong,
                "Generated answer exceeds the configured limit.");
        }

        switch (result.Outcome)
        {
            case GroundedAnswerGenerationOutcome.AnswerGenerated:
                if (string.IsNullOrWhiteSpace(result.Answer))
                {
                    AddIssue(
                        issues,
                        GroundedAnswerValidationIssueCode.MissingAnswer,
                        "Generated answers require answer text.");
                }

                if (result.Citations.Count == 0)
                {
                    AddIssue(
                        issues,
                        GroundedAnswerValidationIssueCode
                            .MissingAnswerCitations,
                        "Generated answers require citations.");
                }

                break;

            case GroundedAnswerGenerationOutcome.InsufficientEvidence:
            case GroundedAnswerGenerationOutcome.Rejected:
                if (!string.IsNullOrWhiteSpace(result.Answer))
                {
                    AddIssue(
                        issues,
                        GroundedAnswerValidationIssueCode.UnexpectedAnswer,
                        "Non-answer outcomes must not contain answer text.");
                }

                if (result.Citations.Count > 0)
                {
                    AddIssue(
                        issues,
                        GroundedAnswerValidationIssueCode
                            .UnexpectedAnswerCitations,
                        "Non-answer outcomes must not contain citations.");
                }

                break;

            default:
                AddIssue(
                    issues,
                    GroundedAnswerValidationIssueCode
                        .InvalidGenerationOutcome,
                    "Result contains an unsupported outcome.");

                break;
        }
    }

    private static void AddIssue(
        List<GroundedAnswerValidationIssue> issues,
        GroundedAnswerValidationIssueCode code,
        string message,
        string? citationId = null)
    {
        issues.Add(
            new GroundedAnswerValidationIssue(
                code,
                message,
                citationId));
    }
}