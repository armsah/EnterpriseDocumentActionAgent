using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EnterpriseDocumentActionAgent.Application.Observability;

/// <summary>
/// Defines shared tracing and metrics instruments
/// for the Enterprise Document Action Agent.
/// </summary>
/// <remarks>
/// This class declares instrumentation only.
///
/// Telemetry collection, sampling, aggregation,
/// and export must be configured by the host.
///
/// Tenant identifiers, document content, prompts,
/// access tokens, and other sensitive information
/// must not be included in metric dimensions.
///
/// Activity tags must also be reviewed for
/// sensitive information before export.
/// </remarks>
public static class EnterpriseDocumentActionAgentTelemetry
{
    /// <summary>
    /// Identifies the application telemetry source.
    /// </summary>
    public const string SourceName =
        "EnterpriseDocumentActionAgent";

    /// <summary>
    /// Identifies the instrumentation version.
    /// </summary>
    public const string InstrumentationVersion =
        "1.0.0";

    /// <summary>
    /// Provides distributed tracing instrumentation.
    /// </summary>
    public static readonly ActivitySource ActivitySource =
        new(
            SourceName,
            InstrumentationVersion);

    /// <summary>
    /// Provides application metrics instrumentation.
    /// </summary>
    public static readonly Meter Meter =
        new(
            SourceName,
            InstrumentationVersion);

    /// <summary>
    /// Counts workflow execution attempts.
    /// </summary>
    public static readonly Counter<long> WorkflowExecutions =
        Meter.CreateCounter<long>(
            "enterprise_agent.workflow.executions",
            unit: "{execution}",
            description:
                "Total workflow execution attempts.");

    /// <summary>
    /// Counts completed workflow executions.
    /// </summary>
    public static readonly Counter<long> WorkflowCompletions =
        Meter.CreateCounter<long>(
            "enterprise_agent.workflow.completions",
            unit: "{execution}",
            description:
                "Total completed workflow executions.");

    /// <summary>
    /// Counts failed workflow executions.
    /// </summary>
    public static readonly Counter<long> WorkflowFailures =
        Meter.CreateCounter<long>(
            "enterprise_agent.workflow.failures",
            unit: "{execution}",
            description:
                "Total failed workflow executions.");

    /// <summary>
    /// Measures workflow execution duration.
    /// </summary>
    public static readonly Histogram<double> WorkflowDuration =
        Meter.CreateHistogram<double>(
            "enterprise_agent.workflow.duration",
            unit: "s",
            description:
                "Workflow execution duration in seconds.");

    /// <summary>
    /// Counts document ingestion attempts.
    /// </summary>
    public static readonly Counter<long> DocumentIngestionAttempts =
        Meter.CreateCounter<long>(
            "enterprise_agent.document.ingestion.attempts",
            unit: "{document}",
            description:
                "Total document ingestion attempts.");

    /// <summary>
    /// Counts successful document ingestion operations.
    /// </summary>
    public static readonly Counter<long> DocumentIngestionCompletions =
        Meter.CreateCounter<long>(
            "enterprise_agent.document.ingestion.completions",
            unit: "{document}",
            description:
                "Total successful document ingestion operations.");

    /// <summary>
    /// Counts failed document ingestion operations.
    /// </summary>
    public static readonly Counter<long> DocumentIngestionFailures =
        Meter.CreateCounter<long>(
            "enterprise_agent.document.ingestion.failures",
            unit: "{document}",
            description:
                "Total failed document ingestion operations.");

    /// <summary>
    /// Measures document ingestion duration.
    /// </summary>
    public static readonly Histogram<double> DocumentIngestionDuration =
        Meter.CreateHistogram<double>(
            "enterprise_agent.document.ingestion.duration",
            unit: "s",
            description:
                "Document ingestion duration in seconds.");

    /// <summary>
    /// Counts tool execution attempts.
    /// </summary>
    public static readonly Counter<long> ToolExecutionAttempts =
        Meter.CreateCounter<long>(
            "enterprise_agent.tool.executions",
            unit: "{execution}",
            description:
                "Total tool execution attempts.");

    /// <summary>
    /// Counts rejected tool execution requests.
    /// </summary>
    public static readonly Counter<long> ToolExecutionRejections =
        Meter.CreateCounter<long>(
            "enterprise_agent.tool.rejections",
            unit: "{execution}",
            description:
                "Total rejected tool execution requests.");

    /// <summary>
    /// Measures tool execution duration.
    /// </summary>
    public static readonly Histogram<double> ToolExecutionDuration =
        Meter.CreateHistogram<double>(
            "enterprise_agent.tool.duration",
            unit: "s",
            description:
                "Tool execution duration in seconds.");

    /// <summary>
    /// Counts approval requests.
    /// </summary>
    public static readonly Counter<long> ApprovalRequests =
        Meter.CreateCounter<long>(
            "enterprise_agent.approval.requests",
            unit: "{request}",
            description:
                "Total approval requests.");

    /// <summary>
    /// Counts rejected approval requests.
    /// </summary>
    public static readonly Counter<long> ApprovalRejections =
        Meter.CreateCounter<long>(
            "enterprise_agent.approval.rejections",
            unit: "{request}",
            description:
                "Total rejected approval requests.");

    /// <summary>
    /// Starts an activity for an application operation.
    /// </summary>
    /// <remarks>
    /// Operation names must come from a controlled,
    /// low-cardinality set.
    ///
    /// Do not use document titles, user questions,
    /// tenant identifiers, or other user-controlled
    /// strings as operation names.
    /// </remarks>
    public static Activity? StartActivity(
        string operationName,
        ActivityKind kind = ActivityKind.Internal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            operationName);

        return ActivitySource.StartActivity(
            operationName,
            kind);
    }

    /// <summary>
    /// Records a successful operation duration.
    /// </summary>
    public static void RecordDuration(
        Histogram<double> histogram,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(histogram);

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                "Duration cannot be negative.");
        }

        histogram.Record(duration.TotalSeconds);
    }

    /// <summary>
    /// Marks an activity as failed without
    /// attaching exception messages or stack traces.
    /// </summary>
    /// <remarks>
    /// Exception messages may contain sensitive
    /// information and should not be added to
    /// telemetry without an explicit data policy.
    /// </remarks>
    public static void MarkFailure(
        Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    /// <summary>
    /// Marks an activity as successful.
    /// </summary>
    public static void MarkSuccess(
        Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}