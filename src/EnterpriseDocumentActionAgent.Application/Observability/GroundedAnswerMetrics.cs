using System.Diagnostics.Metrics;
using EnterpriseDocumentActionAgent.Application.Grounding;

namespace EnterpriseDocumentActionAgent.Application.Observability;

/// <summary>
/// Provides metrics for grounded-answer generation,
/// evidence validation, and prompt-injection screening.
/// </summary>
/// <remarks>
/// All instruments use the shared application Meter.
///
/// Metric dimensions must remain low-cardinality.
///
/// Do not include tenant identifiers, user identifiers,
/// document identifiers, questions, prompts, document
/// content, citation identifiers, or exception messages
/// in metric attributes.
///
/// Metrics are recorded only when the corresponding
/// application operation invokes these methods.
/// </remarks>
public static class GroundedAnswerMetrics
{
    private static readonly Meter SharedMeter =
        EnterpriseDocumentActionAgentTelemetry.Meter;

    /// <summary>
    /// Counts grounded-answer generation attempts.
    /// </summary>
    public static readonly Counter<long> GenerationAttempts =
        SharedMeter.CreateCounter<long>(
            "enterprise_agent.grounding.generation.attempts",
            unit: "{request}",
            description:
                "Total grounded-answer generation attempts.");

    /// <summary>
    /// Counts successfully generated answers.
    /// </summary>
    public static readonly Counter<long> GeneratedAnswers =
        SharedMeter.CreateCounter<long>(
            "enterprise_agent.grounding.generation.completed",
            unit: "{answer}",
            description:
                "Total successfully generated grounded answers.");

    /// <summary>
    /// Counts requests with insufficient evidence.
    /// </summary>
    public static readonly Counter<long> InsufficientEvidence =
        SharedMeter.CreateCounter<long>(
            "enterprise_agent.grounding.insufficient_evidence",
            unit: "{request}",
            description:
                "Total grounded-answer requests with " +
                "insufficient supporting evidence.");

    /// <summary>
    /// Counts rejected generation requests.
    /// </summary>
    public static readonly Counter<long> RejectedRequests =
        SharedMeter.CreateCounter<long>(
            "enterprise_agent.grounding.rejected",
            unit: "{request}",
            description:
                "Total rejected grounded-answer requests.");

    /// <summary>
    /// Counts failed generation operations.
    /// </summary>
    public static readonly Counter<long> GenerationFailures =
        SharedMeter.CreateCounter<long>(
            "enterprise_agent.grounding.generation.failures",
            unit: "{request}",
            description:
                "Total failed grounded-answer generation operations.");

    /// <summary>
    /// Measures generation duration in seconds.
    /// </summary>
    public static readonly Histogram<double> GenerationDuration =
        SharedMeter.CreateHistogram<double>(
            "enterprise_agent.grounding.generation.duration",
            unit: "s",
            description:
                "Grounded-answer generation duration in seconds.");

    /// <summary>
    /// Measures the number of evidence items
    /// supplied to generation.
    /// </summary>
    public static readonly Histogram<int> EvidenceItemCount =
        SharedMeter.CreateHistogram<int>(
            "enterprise_agent.grounding.evidence.items",
            unit: "{item}",
            description:
                "Number of evidence items supplied " +
                "to grounded-answer generation.");

    /// <summary>
    /// Measures the number of citations returned
    /// by generated answers.
    /// </summary>
    public static readonly Histogram<int> CitationCount =
        SharedMeter.CreateHistogram<int>(
            "enterprise_agent.grounding.citations",
            unit: "{citation}",
            description:
                "Number of citations returned " +
                "by grounded-answer generation.");

    /// <summary>
    /// Counts evidence validation failures.
    /// </summary>
    public static readonly Counter<long> EvidenceValidationFailures =
        SharedMeter.CreateCounter<long>(
            "enterprise_agent.grounding.evidence.validation_failures",
            unit: "{failure}",
            description:
                "Total evidence validation failures.");

    /// <summary>
    /// Counts prompt-injection screening rejections.
    /// </summary>
    public static readonly Counter<long> PromptInjectionRejections =
        SharedMeter.CreateCounter<long>(
            "enterprise_agent.grounding.prompt_injection.rejections",
            unit: "{rejection}",
            description:
                "Total evidence items rejected by " +
                "prompt-injection screening.");

    /// <summary>
    /// Records the beginning of a generation attempt.
    /// </summary>
    public static void RecordGenerationAttempt()
    {
        GenerationAttempts.Add(1);
    }

    /// <summary>
    /// Records a grounded-answer generation outcome.
    /// </summary>
    /// <remarks>
    /// Outcomes are mutually exclusive.
    ///
    /// A generated answer increments GeneratedAnswers.
    ///
    /// An insufficient-evidence outcome increments
    /// InsufficientEvidence.
    ///
    /// A rejected outcome increments RejectedRequests.
    ///
    /// Operational failures must be recorded separately
    /// through RecordGenerationFailure.
    /// </remarks>
    public static void RecordGenerationOutcome(
        GroundedAnswerGenerationOutcome outcome)
    {
        switch (outcome)
        {
            case GroundedAnswerGenerationOutcome.AnswerGenerated:
                GeneratedAnswers.Add(1);
                break;

            case GroundedAnswerGenerationOutcome.InsufficientEvidence:
                InsufficientEvidence.Add(1);
                break;

            case GroundedAnswerGenerationOutcome.Rejected:
                RejectedRequests.Add(1);
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(outcome),
                    outcome,
                    "Unsupported grounded-answer generation outcome.");
        }
    }

    /// <summary>
    /// Records an operational generation failure.
    /// </summary>
    public static void RecordGenerationFailure()
    {
        GenerationFailures.Add(1);
    }

    /// <summary>
    /// Records grounded-answer generation duration.
    /// </summary>
    public static void RecordGenerationDuration(
        TimeSpan duration)
    {
        EnterpriseDocumentActionAgentTelemetry.RecordDuration(
            GenerationDuration,
            duration);
    }

    /// <summary>
    /// Records the number of evidence items supplied
    /// to a generation request.
    /// </summary>
    public static void RecordEvidenceItemCount(
        int evidenceItemCount)
    {
        if (evidenceItemCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(evidenceItemCount),
                "Evidence item count cannot be negative.");
        }

        EvidenceItemCount.Record(evidenceItemCount);
    }

    /// <summary>
    /// Records the number of citations returned
    /// by a generation result.
    /// </summary>
    public static void RecordCitationCount(
        int citationCount)
    {
        if (citationCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(citationCount),
                "Citation count cannot be negative.");
        }

        CitationCount.Record(citationCount);
    }

    /// <summary>
    /// Records an evidence validation failure.
    /// </summary>
    public static void RecordEvidenceValidationFailure()
    {
        EvidenceValidationFailures.Add(1);
    }

    /// <summary>
    /// Records a prompt-injection screening rejection.
    /// </summary>
    public static void RecordPromptInjectionRejection()
    {
        PromptInjectionRejections.Add(1);
    }
}