using System.Collections.Concurrent;
using System.Text.Json;

namespace EnterpriseDocumentActionAgent.Application.AI.Tools;

/// <summary>
/// Represents the result of an application tool invocation.
/// </summary>
public sealed record AgentToolResult
{
    public string ToolName { get; }

    public string Output { get; }

    public bool Success { get; }

    public DateTimeOffset ExecutedAtUtc { get; }

    public AgentToolResult(
        string toolName,
        string output,
        bool success,
        DateTimeOffset? executedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException(
                "Tool name is required.",
                nameof(toolName));
        }

        ArgumentNullException.ThrowIfNull(output);

        ToolName = toolName;
        Output = output;
        Success = success;
        ExecutedAtUtc = executedAtUtc ?? DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Defines the asynchronous execution signature
/// for registered application tools.
/// </summary>
public delegate Task<AgentToolResult> AgentToolHandler(
    JsonElement arguments,
    CancellationToken cancellationToken);

/// <summary>
/// Registers and invokes application-level AI agent tools.
/// </summary>
/// <remarks>
/// This dispatcher validates JSON syntax and argument shape,
/// but does not perform complete JSON Schema validation.
///
/// Authorization, approvals, and execution policies must
/// be enforced before dispatching untrusted tool requests.
/// </remarks>
public sealed class AgentToolDispatcher
{
    private sealed record RegisteredTool(
        AgentToolSchema Schema,
        AgentToolHandler Handler);

    private readonly ConcurrentDictionary<string, RegisteredTool>
        _tools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a tool schema and its execution handler.
    /// </summary>
    public void Register(
        AgentToolSchema schema,
        AgentToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(handler);

        var registered = new RegisteredTool(
            schema,
            handler);

        if (!_tools.TryAdd(schema.Name, registered))
        {
            throw new InvalidOperationException(
                $"Tool '{schema.Name}' is already registered.");
        }
    }

    /// <summary>
    /// Invokes a registered tool using JSON arguments.
    /// </summary>
    public async Task<AgentToolResult> DispatchAsync(
        string toolName,
        string argumentsJson,
        CancellationToken cancellationToken = default)
    {
        ValidateToolName(toolName);

        ArgumentNullException.ThrowIfNull(argumentsJson);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_tools.TryGetValue(toolName, out var registered))
        {
            throw new KeyNotFoundException(
                $"Tool '{toolName}' is not registered.");
        }

        var arguments = registered.Schema.ParseArguments(
            argumentsJson);

        cancellationToken.ThrowIfCancellationRequested();

        var result = await registered.Handler(
            arguments,
            cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            throw new InvalidOperationException(
                $"Tool '{toolName}' returned a null result.");
        }

        if (!string.Equals(
                result.ToolName,
                registered.Schema.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Tool result name does not match " +
                "the registered tool.");
        }

        return result;
    }

    /// <summary>
    /// Determines whether a tool is registered.
    /// </summary>
    public bool ContainsTool(string toolName)
    {
        ValidateToolName(toolName);

        return _tools.ContainsKey(toolName);
    }

    /// <summary>
    /// Returns the schema registered for a tool.
    /// </summary>
    public AgentToolSchema GetSchema(string toolName)
    {
        ValidateToolName(toolName);

        if (_tools.TryGetValue(toolName, out var registered))
        {
            return registered.Schema;
        }

        throw new KeyNotFoundException(
            $"Tool '{toolName}' is not registered.");
    }

    /// <summary>
    /// Returns a snapshot of all registered tool schemas.
    /// </summary>
    public IReadOnlyList<AgentToolSchema> GetSchemas()
    {
        return _tools.Values
            .Select(registered => registered.Schema)
            .OrderBy(
                schema => schema.Name,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Removes a registered tool.
    /// </summary>
    public bool Unregister(string toolName)
    {
        ValidateToolName(toolName);

        return _tools.TryRemove(toolName, out _);
    }

    private static void ValidateToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException(
                "Tool name is required.",
                nameof(toolName));
        }

        if (toolName.Length > 128)
        {
            throw new ArgumentException(
                "Tool name cannot exceed 128 characters.",
                nameof(toolName));
        }
    }
}