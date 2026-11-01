namespace EnterpriseDocumentActionAgent.Application.Tools;

/// <summary>
/// Defines a policy for determining whether a tool is allowed.
/// </summary>
public interface IToolAllowList
{
    /// <summary>
    /// Determines whether a tool is explicitly permitted.
    /// </summary>
    bool IsAllowed(string toolName);

    /// <summary>
    /// Ensures that a tool is permitted or throws an exception.
    /// </summary>
    void EnsureAllowed(string toolName);

    /// <summary>
    /// Returns a snapshot of permitted tool names.
    /// </summary>
    IReadOnlyList<string> GetAllowedTools();
}

/// <summary>
/// Represents an immutable allowlist of agent tools.
/// </summary>
/// <remarks>
/// Tool access is denied by default.
///
/// This policy is intended to be enforced by the
/// secure tool execution gateway.
///
/// Tool registration does not imply authorization.
/// </remarks>
public sealed class ToolAllowList : IToolAllowList
{
    private readonly HashSet<string> _allowedTools;

    /// <summary>
    /// Creates an allowlist containing the specified tools.
    /// </summary>
    public ToolAllowList(IEnumerable<string> allowedTools)
    {
        ArgumentNullException.ThrowIfNull(allowedTools);

        _allowedTools = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var toolName in allowedTools)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new ArgumentException(
                    "Allowed tool names cannot be null or empty.",
                    nameof(allowedTools));
            }

            _allowedTools.Add(toolName.Trim());
        }
    }

    /// <summary>
    /// Creates a policy that denies every tool.
    /// </summary>
    public static ToolAllowList DenyAll()
    {
        return new ToolAllowList(Array.Empty<string>());
    }

    /// <summary>
    /// Creates a policy that permits the specified tools.
    /// </summary>
    public static ToolAllowList Allow(
        params string[] toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        return new ToolAllowList(toolNames);
    }

    /// <summary>
    /// Determines whether the specified tool is allowed.
    /// </summary>
    public bool IsAllowed(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return _allowedTools.Contains(toolName.Trim());
    }

    /// <summary>
    /// Rejects tool execution when the tool is not allowed.
    /// </summary>
    public void EnsureAllowed(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (!IsAllowed(toolName))
        {
            throw new UnauthorizedAccessException(
                $"Tool '{toolName}' is not permitted by the allowlist.");
        }
    }

    /// <summary>
    /// Returns an alphabetically sorted snapshot of allowed tools.
    /// </summary>
    public IReadOnlyList<string> GetAllowedTools()
    {
        return _allowedTools
            .OrderBy(
                toolName => toolName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}