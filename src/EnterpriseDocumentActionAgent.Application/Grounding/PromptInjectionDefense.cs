using System.Text.RegularExpressions;

namespace EnterpriseDocumentActionAgent.Application.Grounding;

/// <summary>
/// Identifies categories of suspicious instructions
/// detected within untrusted document evidence.
/// </summary>
public enum PromptInjectionFindingCategory
{
    InstructionOverride = 1,
    RoleSpoofing = 2,
    SecretDisclosure = 3,
    ToolManipulation = 4,
    ExternalDataTransfer = 5,
    OutputManipulation = 6
}

/// <summary>
/// Represents the severity of a detected
/// prompt-injection indicator.
/// </summary>
public enum PromptInjectionFindingSeverity
{
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Defines how suspicious evidence should be handled.
/// </summary>
public enum PromptInjectionHandlingPolicy
{
    /// <summary>
    /// Report findings without blocking evidence.
    /// </summary>
    ReportOnly = 1,

    /// <summary>
    /// Exclude evidence items containing findings.
    /// </summary>
    ExcludeSuspiciousEvidence = 2,

    /// <summary>
    /// Reject the entire evidence collection
    /// when any finding is detected.
    /// </summary>
    RejectOnDetection = 3
}

/// <summary>
/// Represents a detected prompt-injection indicator.
/// </summary>
/// <remarks>
/// Findings intentionally omit the matched document
/// content to reduce accidental disclosure of
/// sensitive text through logs or diagnostics.
/// </remarks>
public sealed record PromptInjectionFinding(
    string CitationId,
    PromptInjectionFindingCategory Category,
    PromptInjectionFindingSeverity Severity,
    string RuleId,
    string Description);

/// <summary>
/// Defines configuration for deterministic
/// prompt-injection screening.
/// </summary>
public sealed record PromptInjectionDefenseOptions(
    PromptInjectionHandlingPolicy HandlingPolicy =
        PromptInjectionHandlingPolicy.RejectOnDetection,
    int MaximumEvidenceItems = 20,
    int MaximumCharactersPerEvidence = 60000,
    int MaximumTotalCharacters = 120000);

/// <summary>
/// Represents the disposition of screened evidence.
/// </summary>
public enum PromptInjectionScreeningDisposition
{
    /// <summary>
    /// No configured indicators were detected.
    /// This does not prove the evidence is safe.
    /// </summary>
    NoIndicatorsDetected = 1,

    /// <summary>
    /// Findings were detected and reported,
    /// but evidence was retained.
    /// </summary>
    FindingsReported = 2,

    /// <summary>
    /// Suspicious evidence was excluded.
    /// </summary>
    EvidenceExcluded = 3,

    /// <summary>
    /// Screening rejected the evidence collection.
    /// </summary>
    Rejected = 4
}

/// <summary>
/// Represents the result of screening
/// untrusted document evidence.
/// </summary>
public sealed record PromptInjectionScreeningResult(
    PromptInjectionScreeningDisposition Disposition,
    IReadOnlyList<GroundedAnswerEvidence> AcceptedEvidence,
    IReadOnlyList<PromptInjectionFinding> Findings)
{
    /// <summary>
    /// Indicates whether processing may continue
    /// under the configured handling policy.
    /// </summary>
    public bool MayContinue =>
        Disposition != PromptInjectionScreeningDisposition.Rejected;
}

/// <summary>
/// Defines deterministic screening of retrieved
/// document evidence for prompt-injection indicators.
/// </summary>
public interface IPromptInjectionDefense
{
    PromptInjectionScreeningResult Screen(
        IReadOnlyList<GroundedAnswerEvidence> evidence);
}

/// <summary>
/// Screens untrusted document evidence using a bounded
/// collection of deterministic detection rules.
/// </summary>
/// <remarks>
/// This service detects selected suspicious patterns.
///
/// It does not establish that evidence is trustworthy,
/// authorized, factually correct, or free of attacks.
///
/// The caller must enforce document authorization,
/// tenant isolation, and trusted instruction boundaries.
///
/// Screening must occur before evidence is supplied
/// to a language-model generation provider.
/// </remarks>
public sealed class PromptInjectionDefense
    : IPromptInjectionDefense
{
    private readonly PromptInjectionDefenseOptions _options;

    private static readonly TimeSpan RegexTimeout =
        TimeSpan.FromMilliseconds(100);

    private static readonly IReadOnlyList<DetectionRule> Rules =
        CreateRules();

    public PromptInjectionDefense(
        PromptInjectionDefenseOptions? options = null)
    {
        _options = options ?? new PromptInjectionDefenseOptions();

        ValidateOptions(_options);
    }

    /// <inheritdoc />
    public PromptInjectionScreeningResult Screen(
        IReadOnlyList<GroundedAnswerEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.Count > _options.MaximumEvidenceItems)
        {
            throw new ArgumentException(
                "Evidence count exceeds the screening limit.",
                nameof(evidence));
        }

        var totalCharacters = 0L;

        var acceptedEvidence =
            new List<GroundedAnswerEvidence>(evidence.Count);

        var findings =
            new List<PromptInjectionFinding>();

        foreach (var item in evidence)
        {
            ValidateEvidence(item);

            if (item.Content.Length >
                _options.MaximumCharactersPerEvidence)
            {
                throw new ArgumentException(
                    "An evidence item exceeds the screening length limit.",
                    nameof(evidence));
            }

            totalCharacters += item.Content.Length;

            if (totalCharacters > _options.MaximumTotalCharacters)
            {
                throw new ArgumentException(
                    "Total evidence content exceeds the screening limit.",
                    nameof(evidence));
            }

            var itemFindings = ScreenItem(item);

            findings.AddRange(itemFindings);

            if (itemFindings.Count == 0 ||
                _options.HandlingPolicy ==
                    PromptInjectionHandlingPolicy.ReportOnly)
            {
                acceptedEvidence.Add(item);
            }
        }

        if (findings.Count == 0)
        {
            return new PromptInjectionScreeningResult(
                PromptInjectionScreeningDisposition.NoIndicatorsDetected,
                acceptedEvidence.ToArray(),
                Array.Empty<PromptInjectionFinding>());
        }

        return _options.HandlingPolicy switch
        {
            PromptInjectionHandlingPolicy.ReportOnly =>
                new PromptInjectionScreeningResult(
                    PromptInjectionScreeningDisposition.FindingsReported,
                    acceptedEvidence.ToArray(),
                    findings.ToArray()),

            PromptInjectionHandlingPolicy.ExcludeSuspiciousEvidence =>
                new PromptInjectionScreeningResult(
                    PromptInjectionScreeningDisposition.EvidenceExcluded,
                    acceptedEvidence.ToArray(),
                    findings.ToArray()),

            PromptInjectionHandlingPolicy.RejectOnDetection =>
                new PromptInjectionScreeningResult(
                    PromptInjectionScreeningDisposition.Rejected,
                    Array.Empty<GroundedAnswerEvidence>(),
                    findings.ToArray()),

            _ => throw new InvalidOperationException(
                "Unsupported prompt-injection handling policy.")
        };
    }

    private static IReadOnlyList<PromptInjectionFinding> ScreenItem(
        GroundedAnswerEvidence evidence)
    {
        var findings = new List<PromptInjectionFinding>();

        foreach (var rule in Rules)
        {
            bool matched;

            try
            {
                matched = rule.Pattern.IsMatch(evidence.Content);
            }
            catch (RegexMatchTimeoutException)
            {
                findings.Add(
                    new PromptInjectionFinding(
                        evidence.CitationId,
                        rule.Category,
                        PromptInjectionFindingSeverity.High,
                        rule.Id,
                        "Evidence screening exceeded its time limit."));

                continue;
            }

            if (matched)
            {
                findings.Add(
                    new PromptInjectionFinding(
                        evidence.CitationId,
                        rule.Category,
                        rule.Severity,
                        rule.Id,
                        rule.Description));
            }
        }

        return findings;
    }

    private static void ValidateEvidence(
        GroundedAnswerEvidence? evidence)
    {
        if (evidence is null ||
            evidence.DocumentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(evidence.TenantId) ||
            string.IsNullOrWhiteSpace(evidence.CitationId) ||
            string.IsNullOrWhiteSpace(evidence.ChunkId) ||
            string.IsNullOrWhiteSpace(evidence.DocumentVersion) ||
            string.IsNullOrWhiteSpace(evidence.Content))
        {
            throw new ArgumentException(
                "Evidence contains an invalid item.",
                nameof(evidence));
        }
    }

    private static void ValidateOptions(
        PromptInjectionDefenseOptions options)
    {
        if (!Enum.IsDefined(options.HandlingPolicy))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Prompt-injection handling policy is invalid.");
        }

        if (options.MaximumEvidenceItems <= 0 ||
            options.MaximumCharactersPerEvidence <= 0 ||
            options.MaximumTotalCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Screening limits must be positive.");
        }

        if (options.MaximumCharactersPerEvidence >
            options.MaximumTotalCharacters)
        {
            throw new ArgumentException(
                "Per-evidence limit cannot exceed the total limit.",
                nameof(options));
        }
    }

    private static IReadOnlyList<DetectionRule> CreateRules()
    {
        return new[]
        {
            CreateRule(
                "PI-001",
                PromptInjectionFindingCategory.InstructionOverride,
                PromptInjectionFindingSeverity.High,
                @"\bignore\s+(all\s+)?(previous|prior|above)\s+instructions\b",
                "Evidence contains an instruction-override phrase."),

            CreateRule(
                "PI-002",
                PromptInjectionFindingCategory.InstructionOverride,
                PromptInjectionFindingSeverity.High,
                @"\b(disregard|override)\s+(the\s+)?(system|developer)\s+(prompt|instructions)\b",
                "Evidence attempts to override trusted instructions."),

            CreateRule(
                "PI-003",
                PromptInjectionFindingCategory.RoleSpoofing,
                PromptInjectionFindingSeverity.High,
                @"(\[\s*system\s*\]|<\s*\|im_start\|>\s*system\b|<\s*system\s*>)",
                "Evidence contains a system-role impersonation marker."),

            CreateRule(
                "PI-004",
                PromptInjectionFindingCategory.RoleSpoofing,
                PromptInjectionFindingSeverity.High,
                @"\b(you\s+are\s+now|act\s+as)\s+(the\s+)?(system|developer)\b",
                "Evidence attempts to impersonate a privileged role."),

            CreateRule(
                "PI-005",
                PromptInjectionFindingCategory.SecretDisclosure,
                PromptInjectionFindingSeverity.Critical,
                @"\b(reveal|print|show|disclose|expose)\s+(the\s+)?(api\s*key|secret|password|credentials|system\s+prompt)\b",
                "Evidence requests disclosure of protected information."),

            CreateRule(
                "PI-006",
                PromptInjectionFindingCategory.ToolManipulation,
                PromptInjectionFindingSeverity.High,
                @"\b(call|invoke|execute|run)\s+(the\s+)?(tool|function|command)\b",
                "Evidence contains an instruction to invoke a tool."),

            CreateRule(
                "PI-007",
                PromptInjectionFindingCategory.ExternalDataTransfer,
                PromptInjectionFindingSeverity.Critical,
                @"\b(send|upload|post|transmit|exfiltrate)\s+.{0,80}\s+to\s+https?://",
                "Evidence contains an external data-transfer instruction."),

            CreateRule(
                "PI-008",
                PromptInjectionFindingCategory.OutputManipulation,
                PromptInjectionFindingSeverity.Medium,
                @"\b(do\s+not|don't)\s+(cite|include\s+citations|mention\s+sources)\b",
                "Evidence attempts to suppress source citations.")
        };
    }

    private static DetectionRule CreateRule(
        string id,
        PromptInjectionFindingCategory category,
        PromptInjectionFindingSeverity severity,
        string pattern,
        string description)
    {
        return new DetectionRule(
            id,
            category,
            severity,
            new Regex(
                pattern,
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled,
                RegexTimeout),
            description);
    }

    private sealed record DetectionRule(
        string Id,
        PromptInjectionFindingCategory Category,
        PromptInjectionFindingSeverity Severity,
        Regex Pattern,
        string Description);
}