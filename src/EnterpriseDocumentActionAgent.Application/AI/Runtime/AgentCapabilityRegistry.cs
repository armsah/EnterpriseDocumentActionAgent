using System.Collections.Concurrent;
using System.Collections.Frozen;

namespace EnterpriseDocumentActionAgent.Application.AI.Runtime;

/// <summary>
/// Registers and discovers application-level agent capabilities.
/// </summary>
/// <remarks>
/// Agent capabilities describe the tasks an agent is configured
/// to perform, independently of its underlying AI model.
///
/// Capability registration is descriptive metadata and must
/// not be treated as an authorization mechanism.
/// </remarks>
public sealed class AgentCapabilityRegistry
{
    private readonly ConcurrentDictionary<string, FrozenSet<string>>
        _agents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers an agent and its declared capabilities.
    /// </summary>
    public void Register(
        string agentId,
        IEnumerable<string> capabilities)
    {
        ValidateIdentifier(agentId, nameof(agentId));

        ArgumentNullException.ThrowIfNull(capabilities);

        var capabilitySet = CreateCapabilitySet(
            capabilities,
            nameof(capabilities));

        if (!_agents.TryAdd(agentId, capabilitySet))
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' is already registered.");
        }
    }

    /// <summary>
    /// Retrieves the capabilities registered for an agent.
    /// </summary>
    public IReadOnlySet<string> GetCapabilities(string agentId)
    {
        ValidateIdentifier(agentId, nameof(agentId));

        if (_agents.TryGetValue(agentId, out var capabilities))
        {
            return capabilities;
        }

        throw new KeyNotFoundException(
            $"Agent '{agentId}' is not registered.");
    }

    /// <summary>
    /// Determines whether an agent is registered.
    /// </summary>
    public bool ContainsAgent(string agentId)
    {
        ValidateIdentifier(agentId, nameof(agentId));

        return _agents.ContainsKey(agentId);
    }

    /// <summary>
    /// Determines whether an agent supports one capability.
    /// </summary>
    public bool Supports(
        string agentId,
        string capability)
    {
        ValidateIdentifier(capability, nameof(capability));

        return GetCapabilities(agentId).Contains(capability);
    }

    /// <summary>
    /// Determines whether an agent supports all
    /// requested capabilities.
    /// </summary>
    public bool SupportsAll(
        string agentId,
        IEnumerable<string> requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(requiredCapabilities);

        var required = CreateCapabilitySet(
            requiredCapabilities,
            nameof(requiredCapabilities));

        var supported = GetCapabilities(agentId);

        return required.All(supported.Contains);
    }

    /// <summary>
    /// Finds registered agents that support every
    /// requested capability.
    /// </summary>
    public IReadOnlyList<string> FindAgents(
        IEnumerable<string> requiredCapabilities)
    {
        ArgumentNullException.ThrowIfNull(requiredCapabilities);

        var required = CreateCapabilitySet(
            requiredCapabilities,
            nameof(requiredCapabilities));

        return _agents
            .Where(entry =>
                required.All(entry.Value.Contains))
            .Select(entry => entry.Key)
            .OrderBy(
                agentId => agentId,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns all registered agent identifiers.
    /// </summary>
    public IReadOnlyList<string> GetAgentIds()
    {
        return _agents.Keys
            .OrderBy(
                agentId => agentId,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Removes an agent's capability registration.
    /// </summary>
    public bool Unregister(string agentId)
    {
        ValidateIdentifier(agentId, nameof(agentId));

        return _agents.TryRemove(agentId, out _);
    }

    /// <summary>
    /// Creates an immutable, case-insensitive
    /// capability collection.
    /// </summary>
    private static FrozenSet<string> CreateCapabilitySet(
        IEnumerable<string> capabilities,
        string parameterName)
    {
        var unique = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var capability in capabilities)
        {
            ValidateIdentifier(capability, parameterName);

            unique.Add(capability);
        }

        return unique.ToFrozenSet(
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates agent and capability identifiers.
    /// </summary>
    private static void ValidateIdentifier(
        string? identifier,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException(
                "Identifier is required.",
                parameterName);
        }

        if (identifier.Length > 200)
        {
            throw new ArgumentException(
                "Identifier cannot exceed 200 characters.",
                parameterName);
        }
    }
}