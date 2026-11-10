using System.Collections.Frozen;

namespace EnterpriseDocumentActionAgent.Application.AI.Teams;

/// <summary>
/// Represents a request for supervisor-based
/// agent selection within a team.
/// </summary>
/// <remarks>
/// A request describes the task and optional
/// routing constraints.
///
/// It does not contain the document payload
/// or grant permission to execute the task.
/// </remarks>
public sealed record AgentSupervisorRequest
{
    /// <summary>
    /// Gets the unique task identifier.
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// Gets the task description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets an optional required agent role.
    /// </summary>
    public string? RequiredRole { get; }

    /// <summary>
    /// Gets an optional preferred agent identifier.
    /// </summary>
    public string? PreferredAgentId { get; }

    /// <summary>
    /// Gets whether the designated supervisor
    /// may be selected as a worker.
    /// </summary>
    public bool AllowSupervisorSelection { get; }

    /// <summary>
    /// Gets agent identifiers excluded
    /// from selection.
    /// </summary>
    public IReadOnlySet<string> ExcludedAgentIds { get; }

    /// <summary>
    /// Initializes a supervisor routing request.
    /// </summary>
    public AgentSupervisorRequest(
        string taskId,
        string description,
        string? requiredRole = null,
        string? preferredAgentId = null,
        bool allowSupervisorSelection = false,
        IEnumerable<string>? excludedAgentIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        TaskId = taskId.Trim();
        Description = description.Trim();

        RequiredRole = string.IsNullOrWhiteSpace(requiredRole)
            ? null
            : requiredRole.Trim();

        PreferredAgentId =
            string.IsNullOrWhiteSpace(preferredAgentId)
                ? null
                : preferredAgentId.Trim();

        AllowSupervisorSelection =
            allowSupervisorSelection;

        var exclusions = new HashSet<string>(
            StringComparer.Ordinal);

        if (excludedAgentIds is not null)
        {
            foreach (var agentId in excludedAgentIds)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(
                    agentId);

                exclusions.Add(agentId.Trim());
            }
        }

        ExcludedAgentIds = exclusions.ToFrozenSet(
            StringComparer.Ordinal);
    }
}

/// <summary>
/// Represents a supervisor's agent-selection
/// decision for a task.
/// </summary>
/// <remarks>
/// The decision records routing metadata.
///
/// It does not execute the selected agent
/// or prove that the agent is authorized
/// to perform the requested task.
/// </remarks>
public sealed record AgentSupervisorDecision
{
    /// <summary>
    /// Gets the team identifier.
    /// </summary>
    public string TeamId { get; }

    /// <summary>
    /// Gets the task identifier.
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// Gets the selected agent identifier.
    /// </summary>
    public string SelectedAgentId { get; }

    /// <summary>
    /// Gets the selected agent role.
    /// </summary>
    public string SelectedAgentRole { get; }

    /// <summary>
    /// Gets the routing explanation.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the number of eligible candidates.
    /// </summary>
    public int EligibleCandidateCount { get; }

    /// <summary>
    /// Gets whether a preferred agent
    /// was selected.
    /// </summary>
    public bool UsedPreferredAgent { get; }

    /// <summary>
    /// Gets the decision creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Initializes a supervisor decision.
    /// </summary>
    public AgentSupervisorDecision(
        string teamId,
        string taskId,
        string selectedAgentId,
        string selectedAgentRole,
        string reason,
        int eligibleCandidateCount,
        bool usedPreferredAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            selectedAgentId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            selectedAgentRole);

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (eligibleCandidateCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eligibleCandidateCount));
        }

        TeamId = teamId.Trim();
        TaskId = taskId.Trim();

        SelectedAgentId = selectedAgentId.Trim();
        SelectedAgentRole = selectedAgentRole.Trim();

        Reason = reason.Trim();

        EligibleCandidateCount =
            eligibleCandidateCount;

        UsedPreferredAgent = usedPreferredAgent;

        CreatedAtUtc = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Coordinates deterministic agent selection
/// within a multi-agent team.
/// </summary>
/// <remarks>
/// The supervisor selects an eligible team
/// member using explicit routing constraints.
///
/// Selection order:
///
/// 1. Filter by required role.
/// 2. Apply agent exclusions.
/// 3. Exclude the supervisor unless allowed.
/// 4. Honor an eligible preferred agent.
/// 5. Otherwise select by ordinal agent ID.
///
/// This class does not invoke an AI model,
/// execute an agent, or delegate tasks.
///
/// Team membership may change after a
/// routing decision is produced.
///
/// Authorization and availability checks
/// must be performed by downstream services.
/// </remarks>
public sealed class AgentSupervisor
{
    private readonly AgentTeam _team;

    /// <summary>
    /// Gets the team being coordinated.
    /// </summary>
    public AgentTeam Team => _team;

    /// <summary>
    /// Gets the team identifier.
    /// </summary>
    public string TeamId => _team.TeamId;

    /// <summary>
    /// Initializes a team supervisor.
    /// </summary>
    public AgentSupervisor(
        AgentTeam team)
    {
        ArgumentNullException.ThrowIfNull(team);

        _team = team;
    }

    /// <summary>
    /// Selects an eligible agent for a task.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no eligible team member exists.
    /// </exception>
    public AgentSupervisorDecision SelectAgent(
        AgentSupervisorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var members = _team.GetMembers();

        var supervisorAgentId =
            _team.SupervisorAgentId;

        var eligibleMembers = members
            .Where(member =>
                IsEligible(
                    member,
                    request,
                    supervisorAgentId))
            .OrderBy(
                member => member.AgentId,
                StringComparer.Ordinal)
            .ToArray();

        if (eligibleMembers.Length == 0)
        {
            throw new InvalidOperationException(
                $"No eligible agent is available " +
                $"for task '{request.TaskId}' " +
                $"in team '{_team.TeamId}'.");
        }

        AgentTeamMember selectedMember;

        var usedPreferredAgent = false;

        if (request.PreferredAgentId is not null)
        {
            var preferredMember = eligibleMembers
                .FirstOrDefault(member =>
                    string.Equals(
                        member.AgentId,
                        request.PreferredAgentId,
                        StringComparison.Ordinal));

            if (preferredMember is not null)
            {
                selectedMember = preferredMember;

                usedPreferredAgent = true;
            }
            else
            {
                selectedMember = eligibleMembers[0];
            }
        }
        else
        {
            selectedMember = eligibleMembers[0];
        }

        var reason = usedPreferredAgent
            ? $"Preferred agent '{selectedMember.AgentId}' " +
              "satisfied the routing constraints."
            : $"Agent '{selectedMember.AgentId}' " +
              "was selected using deterministic " +
              "team routing.";

        return new AgentSupervisorDecision(
            teamId: _team.TeamId,
            taskId: request.TaskId,
            selectedAgentId: selectedMember.AgentId,
            selectedAgentRole: selectedMember.Role,
            reason: reason,
            eligibleCandidateCount:
                eligibleMembers.Length,
            usedPreferredAgent: usedPreferredAgent);
    }

    /// <summary>
    /// Asynchronously selects an eligible agent.
    /// </summary>
    /// <remarks>
    /// Routing is currently synchronous and
    /// does not perform external I/O.
    ///
    /// This method exists to support future
    /// asynchronous routing strategies.
    /// </remarks>
    public Task<AgentSupervisorDecision> SelectAgentAsync(
        AgentSupervisorRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var decision = SelectAgent(request);

        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(decision);
    }

    /// <summary>
    /// Returns eligible members for a request
    /// without selecting an agent.
    /// </summary>
    public IReadOnlyList<AgentTeamMember>
        GetEligibleMembers(
            AgentSupervisorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var supervisorAgentId =
            _team.SupervisorAgentId;

        var members = _team.GetMembers()
            .Where(member =>
                IsEligible(
                    member,
                    request,
                    supervisorAgentId))
            .OrderBy(
                member => member.AgentId,
                StringComparer.Ordinal)
            .ToArray();

        return Array.AsReadOnly(members);
    }

    /// <summary>
    /// Determines whether a team member
    /// satisfies routing constraints.
    /// </summary>
    private static bool IsEligible(
        AgentTeamMember member,
        AgentSupervisorRequest request,
        string? supervisorAgentId)
    {
        if (request.RequiredRole is not null &&
            !string.Equals(
                member.Role,
                request.RequiredRole,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.ExcludedAgentIds.Contains(
            member.AgentId))
        {
            return false;
        }

        if (!request.AllowSupervisorSelection &&
            supervisorAgentId is not null &&
            string.Equals(
                member.AgentId,
                supervisorAgentId,
                StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}