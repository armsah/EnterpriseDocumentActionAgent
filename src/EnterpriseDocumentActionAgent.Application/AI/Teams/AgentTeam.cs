using EnterpriseDocumentActionAgent.Application.AI.Runtime;

namespace EnterpriseDocumentActionAgent.Application.AI.Teams;

/// <summary>
/// Represents a registered member of an agent team.
/// </summary>
/// <remarks>
/// Each member references an existing agent configuration.
///
/// The role describes the member's responsibility
/// within the team.
///
/// This record does not execute the agent
/// or grant execution permissions.
/// </remarks>
public sealed record AgentTeamMember
{
    /// <summary>
    /// Gets the agent configuration.
    /// </summary>
    public AgentConfiguration Configuration { get; }

    /// <summary>
    /// Gets the agent identifier.
    /// </summary>
    public string AgentId => Configuration.AgentId;

    /// <summary>
    /// Gets the member's role.
    /// </summary>
    public string Role { get; }

    /// <summary>
    /// Gets an optional member description.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the registration timestamp.
    /// </summary>
    public DateTimeOffset RegisteredAtUtc { get; }

    /// <summary>
    /// Initializes an agent team member.
    /// </summary>
    public AgentTeamMember(
        AgentConfiguration configuration,
        string role,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        Configuration = configuration;

        Role = role.Trim();

        Description = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();

        RegisteredAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Manages the composition of a team
/// of specialized AI agents.
/// </summary>
/// <remarks>
/// A team maintains registered agent configurations,
/// member roles, and an optional supervisor.
///
/// Team membership operations are synchronized.
///
/// Returned member collections are snapshots.
///
/// The team does not execute agents,
/// delegate tasks, or enforce authorization.
/// </remarks>
public sealed class AgentTeam
{
    private readonly object _sync = new();

    private readonly Dictionary<string, AgentTeamMember>
        _members = new(StringComparer.Ordinal);

    private string? _supervisorAgentId;

    /// <summary>
    /// Gets the unique team identifier.
    /// </summary>
    public string TeamId { get; }

    /// <summary>
    /// Gets the team name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the optional team description.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the team creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets the number of registered agents.
    /// </summary>
    public int MemberCount
    {
        get
        {
            lock (_sync)
            {
                return _members.Count;
            }
        }
    }

    /// <summary>
    /// Gets the designated supervisor agent ID.
    /// </summary>
    public string? SupervisorAgentId
    {
        get
        {
            lock (_sync)
            {
                return _supervisorAgentId;
            }
        }
    }

    /// <summary>
    /// Gets whether the team has a supervisor.
    /// </summary>
    public bool HasSupervisor
    {
        get
        {
            lock (_sync)
            {
                return _supervisorAgentId is not null;
            }
        }
    }

    /// <summary>
    /// Initializes an agent team.
    /// </summary>
    public AgentTeam(
        string teamId,
        string name,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        TeamId = teamId.Trim();

        Name = name.Trim();

        Description = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();

        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Registers an agent as a team member.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the agent is already registered.
    /// </exception>
    public AgentTeam AddMember(
        AgentConfiguration configuration,
        string role,
        string? description = null)
    {
        var member = new AgentTeamMember(
            configuration,
            role,
            description);

        return AddMember(member);
    }

    /// <summary>
    /// Registers an existing team member.
    /// </summary>
    public AgentTeam AddMember(
        AgentTeamMember member)
    {
        ArgumentNullException.ThrowIfNull(member);

        lock (_sync)
        {
            if (!_members.TryAdd(member.AgentId, member))
            {
                throw new InvalidOperationException(
                    $"Agent '{member.AgentId}' is already " +
                    $"registered in team '{TeamId}'.");
            }
        }

        return this;
    }

    /// <summary>
    /// Registers multiple team members.
    /// </summary>
    /// <remarks>
    /// Registration is performed sequentially.
    ///
    /// If registration fails, members added
    /// before the failure remain registered.
    /// </remarks>
    public AgentTeam AddMembers(
        IEnumerable<AgentTeamMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        foreach (var member in members)
        {
            AddMember(member);
        }

        return this;
    }

    /// <summary>
    /// Gets a registered team member.
    /// </summary>
    public AgentTeamMember GetMember(
        string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        lock (_sync)
        {
            if (_members.TryGetValue(agentId, out var member))
            {
                return member;
            }
        }

        throw new KeyNotFoundException(
            $"Agent '{agentId}' is not registered " +
            $"in team '{TeamId}'.");
    }

    /// <summary>
    /// Attempts to retrieve a registered member.
    /// </summary>
    public bool TryGetMember(
        string agentId,
        out AgentTeamMember? member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        lock (_sync)
        {
            return _members.TryGetValue(
                agentId,
                out member);
        }
    }

    /// <summary>
    /// Gets whether an agent belongs to the team.
    /// </summary>
    public bool ContainsMember(
        string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        lock (_sync)
        {
            return _members.ContainsKey(agentId);
        }
    }

    /// <summary>
    /// Returns a snapshot of registered members.
    /// </summary>
    public IReadOnlyList<AgentTeamMember> GetMembers()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(
                _members.Values.ToArray());
        }
    }

    /// <summary>
    /// Returns members matching a specific role.
    /// </summary>
    public IReadOnlyList<AgentTeamMember> GetMembersByRole(
        string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        lock (_sync)
        {
            return Array.AsReadOnly(
                _members.Values
                    .Where(member =>
                        string.Equals(
                            member.Role,
                            role,
                            StringComparison.OrdinalIgnoreCase))
                    .ToArray());
        }
    }

    /// <summary>
    /// Designates a registered member
    /// as the team supervisor.
    /// </summary>
    public AgentTeam SetSupervisor(
        string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        lock (_sync)
        {
            if (!_members.ContainsKey(agentId))
            {
                throw new InvalidOperationException(
                    $"Cannot designate agent '{agentId}' " +
                    "as supervisor because the agent " +
                    "is not registered in the team.");
            }

            _supervisorAgentId = agentId;
        }

        return this;
    }

    /// <summary>
    /// Gets the designated supervisor member.
    /// </summary>
    /// <returns>
    /// The supervisor member, or null
    /// when no supervisor is configured.
    /// </returns>
    public AgentTeamMember? GetSupervisor()
    {
        lock (_sync)
        {
            if (_supervisorAgentId is null)
            {
                return null;
            }

            return _members[_supervisorAgentId];
        }
    }

    /// <summary>
    /// Clears the supervisor designation.
    /// </summary>
    public AgentTeam ClearSupervisor()
    {
        lock (_sync)
        {
            _supervisorAgentId = null;
        }

        return this;
    }

    /// <summary>
    /// Removes a registered team member.
    /// </summary>
    /// <remarks>
    /// Removing the supervisor also clears
    /// the supervisor designation.
    /// </remarks>
    /// <returns>
    /// True when a member was removed;
    /// otherwise false.
    /// </returns>
    public bool RemoveMember(
        string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        lock (_sync)
        {
            if (!_members.Remove(agentId))
            {
                return false;
            }

            if (string.Equals(
                _supervisorAgentId,
                agentId,
                StringComparison.Ordinal))
            {
                _supervisorAgentId = null;
            }

            return true;
        }
    }

    /// <summary>
    /// Removes all team members
    /// and clears the supervisor.
    /// </summary>
    public AgentTeam ClearMembers()
    {
        lock (_sync)
        {
            _members.Clear();

            _supervisorAgentId = null;
        }

        return this;
    }
}