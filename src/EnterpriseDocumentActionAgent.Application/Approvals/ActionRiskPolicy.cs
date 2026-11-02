namespace EnterpriseDocumentActionAgent.Application.Approvals;

/// <summary>
/// Represents the risk level associated with an agent action.
/// </summary>
public enum ActionRiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

/// <summary>
/// Describes an action that requires risk evaluation.
/// </summary>
public sealed record ActionRiskRequest(
    string ToolName,
    ApprovalActionType ActionType);

/// <summary>
/// Represents the result of evaluating action risk.
/// </summary>
public sealed record ActionRiskAssessment(
    ActionRiskLevel RiskLevel,
    bool RequiresApproval,
    string Reason);

/// <summary>
/// Defines the contract for evaluating action risk.
/// </summary>
public interface IActionRiskPolicy
{
    /// <summary>
    /// Evaluates the risk associated with an action.
    /// </summary>
    ActionRiskAssessment Evaluate(ActionRiskRequest request);
}

/// <summary>
/// Provides a configurable, conservative risk policy
/// for agent tool execution.
/// </summary>
/// <remarks>
/// Unknown tools require approval by default.
///
/// Tool names and action types must be derived from trusted
/// server-side tool definitions or policy configuration.
///
/// This policy does not authenticate users or authorize
/// access to tenant resources.
///
/// Risk classification is advisory until enforced by
/// the tool execution gateway and approval service.
/// </remarks>
public sealed class ActionRiskPolicy : IActionRiskPolicy
{
    private readonly HashSet<string> _readOnlyTools;
    private readonly HashSet<string> _approvalRequiredTools;

    /// <summary>
    /// Creates a risk policy with explicit tool classifications.
    /// </summary>
    public ActionRiskPolicy(
        IEnumerable<string>? readOnlyTools = null,
        IEnumerable<string>? approvalRequiredTools = null)
    {
        _readOnlyTools = CreateToolSet(readOnlyTools);

        _approvalRequiredTools = CreateToolSet(
            approvalRequiredTools);

        var conflictingTools = _readOnlyTools
            .Intersect(
                _approvalRequiredTools,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (conflictingTools.Length > 0)
        {
            throw new ArgumentException(
                "A tool cannot be classified as both read-only " +
                "and approval-required.");
        }
    }

    /// <inheritdoc />
    public ActionRiskAssessment Evaluate(
        ActionRiskRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return new ActionRiskAssessment(
                ActionRiskLevel.High,
                RequiresApproval: true,
                Reason: "An unidentified tool requires approval.");
        }

        var toolName = request.ToolName.Trim();

        // Explicit approval requirements take precedence
        // over all other tool-name classifications.
        if (_approvalRequiredTools.Contains(toolName))
        {
            return new ActionRiskAssessment(
                ActionRiskLevel.High,
                RequiresApproval: true,
                Reason: "The tool is explicitly classified " +
                        "as approval-required.");
        }

        // Destructive operations always require approval.
        if (request.ActionType == ApprovalActionType.DocumentDeletion)
        {
            return new ActionRiskAssessment(
                ActionRiskLevel.Critical,
                RequiresApproval: true,
                Reason: "Document deletion is a critical action.");
        }

        // Modifying existing documents requires approval.
        if (request.ActionType == ApprovalActionType.DocumentModification)
        {
            return new ActionRiskAssessment(
                ActionRiskLevel.High,
                RequiresApproval: true,
                Reason: "Document modification requires approval.");
        }

        // External actions can affect systems outside
        // the current application boundary.
        if (request.ActionType == ApprovalActionType.ExternalAction)
        {
            return new ActionRiskAssessment(
                ActionRiskLevel.High,
                RequiresApproval: true,
                Reason: "External actions require approval.");
        }

        // Only explicitly registered read-only tools
        // qualify for low-risk classification.
        if (_readOnlyTools.Contains(toolName) &&
            request.ActionType == ApprovalActionType.ToolExecution)
        {
            return new ActionRiskAssessment(
                ActionRiskLevel.Low,
                RequiresApproval: false,
                Reason: "The tool is explicitly classified " +
                        "as read-only.");
        }

        // Unknown or unclassified actions are treated
        // conservatively and require human approval.
        return new ActionRiskAssessment(
            ActionRiskLevel.High,
            RequiresApproval: true,
            Reason: "The action has no trusted low-risk " +
                    "classification.");
    }

    private static HashSet<string> CreateToolSet(
        IEnumerable<string>? toolNames)
    {
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        if (toolNames is null)
        {
            return result;
        }

        foreach (var toolName in toolNames)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new ArgumentException(
                    "Tool names cannot be null or empty.",
                    nameof(toolNames));
            }

            result.Add(toolName.Trim());
        }

        return result;
    }
}