namespace EnterpriseDocumentActionAgent.Application.Grounding;

/// <summary>
/// Represents an evidence excerpt supplied to a
/// grounded-answer generation provider.
/// </summary>
/// <remarks>
/// Evidence is untrusted source content.
///
/// Implementations must treat evidence as data,
/// not as instructions that can override system
/// policies, authorization, or tool restrictions.
///
/// The calling application must enforce tenant
/// isolation and document access permissions before
/// constructing this object.
/// </remarks>
public sealed record GroundedAnswerEvidence(
    string CitationId,
    Guid DocumentId,
    string TenantId,
    string DocumentVersion,
    string ChunkId,
    string Content,
    string? DocumentTitle = null,
    string? SourceUri = null);

/// <summary>
/// Represents a citation returned by a grounded-answer
/// generation provider.
/// </summary>
/// <remarks>
/// Citation identifiers must correspond to evidence
/// identifiers supplied in the generation request.
///
/// A citation is not proof that the referenced content
/// actually supports the generated statement.
/// Grounding verification is a separate responsibility.
/// </remarks>
public sealed record GroundedAnswerCitation(
    string CitationId,
    Guid DocumentId,
    string ChunkId);

/// <summary>
/// Describes the result of attempting to generate
/// an answer from retrieved evidence.
/// </summary>
public enum GroundedAnswerGenerationOutcome
{
    /// <summary>
    /// The provider generated an answer.
    /// </summary>
    AnswerGenerated = 1,

    /// <summary>
    /// The available evidence was insufficient
    /// to produce a supported answer.
    /// </summary>
    InsufficientEvidence = 2,

    /// <summary>
    /// The request was rejected because it could
    /// not be handled under the applicable policy.
    /// </summary>
    Rejected = 3
}

/// <summary>
/// Defines generation settings that may be applied
/// by a grounded-answer provider.
/// </summary>
public sealed record GroundedAnswerGenerationOptions(
    int MaximumAnswerCharacters = 8000,
    int MaximumEvidenceItems = 20,
    int MaximumEvidenceCharacters = 60000,
    double Temperature = 0.0);

/// <summary>
/// Represents an application request to generate
/// an answer from a bounded set of document evidence.
/// </summary>
/// <param name="RequestId">
/// Correlation identifier for this generation request.
/// </param>
/// <param name="TenantId">
/// Tenant identifier used for isolation.
/// </param>
/// <param name="Question">
/// The user's question.
/// </param>
/// <param name="Evidence">
/// Retrieved excerpts already authorized for the caller.
/// </param>
/// <param name="Options">
/// Optional generation settings.
/// </param>
public sealed record GenerateGroundedAnswerRequest(
    Guid RequestId,
    string TenantId,
    string Question,
    IReadOnlyList<GroundedAnswerEvidence> Evidence,
    GroundedAnswerGenerationOptions? Options = null);

/// <summary>
/// Represents the response returned by a
/// grounded-answer generation provider.
/// </summary>
/// <remarks>
/// Answer text is not automatically trusted.
///
/// Downstream components must verify citations,
/// apply output validation, and enforce the
/// application's response policy.
/// </remarks>
public sealed record GenerateGroundedAnswerResult(
    Guid RequestId,
    string TenantId,
    GroundedAnswerGenerationOutcome Outcome,
    string? Answer,
    IReadOnlyList<GroundedAnswerCitation> Citations,
    string Provider,
    string Model,
    DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Defines the application-facing abstraction for
/// evidence-grounded answer generation.
/// </summary>
/// <remarks>
/// Implementations must:
///
/// - Keep document evidence separate from trusted
///   instructions.
/// - Avoid treating retrieved content as executable
///   commands or policy overrides.
/// - Avoid fabricating citations.
/// - Return insufficient-evidence outcomes when
///   the provided excerpts do not support an answer.
/// - Honor cancellation requests.
///
/// The interface does not itself enforce these
/// requirements. Concrete implementations and
/// downstream verification must do so.
/// </remarks>
public interface IGroundedAnswerGenerator
{
    /// <summary>
    /// Generates an answer using the supplied
    /// document evidence.
    /// </summary>
    /// <param name="request">
    /// The generation request.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token for the operation.
    /// </param>
    /// <returns>
    /// A generated answer or an explicit
    /// non-answer outcome.
    /// </returns>
    Task<GenerateGroundedAnswerResult> GenerateAsync(
        GenerateGroundedAnswerRequest request,
        CancellationToken cancellationToken = default);
}