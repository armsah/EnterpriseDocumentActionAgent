using System.Collections.Concurrent;

namespace EnterpriseDocumentActionAgent.Application.Tools;

/// <summary>
/// Defines operations for registering and resolving agent tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Registers a tool under its unique name.
    /// </summary>
    void Register(IAgentTool tool);

    /// <summary>
    /// Attempts to retrieve a registered tool by name.
    /// </summary>
    bool TryGetTool(
        string toolName,
        out IAgentTool? tool);

    /// <summary>
    /// Retrieves a registered tool or throws if it is missing.
    /// </summary>
    IAgentTool GetRequiredTool(string toolName);

    /// <summary>
    /// Returns metadata describing all registered tools.
    /// </summary>
    IReadOnlyList<ToolDefinition> GetDefinitions();

    /// <summary>
    /// Executes a registered tool.
    /// </summary>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Maintains a thread-safe registry of available agent tools.
/// </summary>
/// <remarks>
/// This class provides tool discovery and dispatch only.
///
/// It does not perform authentication, authorization,
/// allowlist enforcement, or approval checks.
///
/// Agent-generated tool calls must pass through the
/// security gateway before reaching this registry.
/// </remarks>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly ConcurrentDictionary<string, IAgentTool> _tools =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a tool using its declared name.
    /// </summary>
    public void Register(IAgentTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition
            ?? throw new ArgumentException(
                "Tool definition cannot be null.",
                nameof(tool));

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException(
                "Tool name cannot be empty.",
                nameof(tool));
        }

        var toolName = definition.Name.Trim();

        if (!_tools.TryAdd(toolName, tool))
        {
            throw new InvalidOperationException(
                $"A tool named '{toolName}' is already registered.");
        }
    }

    /// <summary>
    /// Attempts to find a tool by name.
    /// </summary>
    public bool TryGetTool(
        string toolName,
        out IAgentTool? tool)
    {
        tool = null;

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return _tools.TryGetValue(
            toolName.Trim(),
            out tool);
    }

    /// <summary>
    /// Gets a tool by name, throwing when it is unavailable.
    /// </summary>
    public IAgentTool GetRequiredTool(string toolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        if (TryGetTool(toolName, out var tool) && tool is not null)
        {
            return tool;
        }

        throw new KeyNotFoundException(
            $"Tool '{toolName}' is not registered.");
    }

    /// <summary>
    /// Returns a snapshot of registered tool definitions.
    /// </summary>
    public IReadOnlyList<ToolDefinition> GetDefinitions()
    {
        return _tools.Values
            .Select(tool => tool.Definition)
            .OrderBy(
                definition => definition.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Dispatches a request to a registered tool.
    /// </summary>
    /// <remarks>
    /// This method must only be called after the caller
    /// has completed all required security checks.
    /// </remarks>
    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        ArgumentException.ThrowIfNullOrWhiteSpace(request.ToolName);

        ArgumentNullException.ThrowIfNull(request.Context);

        var tool = GetRequiredTool(request.ToolName);

        return await tool.ExecuteAsync(
            request,
            cancellationToken);
    }
}