using EnterpriseDocumentActionAgent.Application.Agents;
using EnterpriseDocumentActionAgent.Domain.Agents;

namespace EnterpriseDocumentActionAgent.Api;

/// <summary>
/// Represents the authenticated identity associated
/// with an incoming agent API request.
/// </summary>
/// <remarks>
/// Identity values must originate from a trusted
/// authentication and authorization boundary.
///
/// Client-supplied tenant or subject identifiers
/// must never be treated as authenticated identity.
/// </remarks>
public sealed record AgentRequestIdentity(
    string TenantId,
    string SubjectId);

/// <summary>
/// Resolves authenticated request identity
/// for agent API operations.
/// </summary>
public interface IAgentRequestIdentityProvider
{
    Task<AgentRequestIdentity?> GetIdentityAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an API request to create an agent.
/// </summary>
public sealed record CreateAgentApiRequest(
    string Name,
    string? Description,
    int MaximumReasoningSteps = 10,
    int MaximumToolExecutions = 5);

/// <summary>
/// Represents an API request to update agent metadata.
/// </summary>
public sealed record UpdateAgentMetadataApiRequest(
    string Name,
    string? Description);

/// <summary>
/// Represents an API request to update agent limits.
/// </summary>
public sealed record UpdateAgentLimitsApiRequest(
    int MaximumReasoningSteps,
    int MaximumToolExecutions);

/// <summary>
/// Represents an API request to execute an agent.
/// </summary>
public sealed record ExecuteAgentApiRequest(
    string UserRequest);

/// <summary>
/// Represents an agent returned by the API.
/// </summary>
public sealed record AgentApiResponse(
    Guid Id,
    string Name,
    string Description,
    AgentStatus Status,
    int MaximumReasoningSteps,
    int MaximumToolExecutions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Represents the result of an agent execution.
/// </summary>
public sealed record AgentExecutionApiResponse(
    Guid AgentId,
    AgentExecutionStatus Status,
    string? FinalAnswer,
    int ReasoningSteps,
    int ToolExecutions);

/// <summary>
/// Defines the authorized application operations
/// exposed by the agent HTTP API.
/// </summary>
/// <remarks>
/// Implementations must enforce:
///
/// - Authentication and tenant authorization.
/// - Agent ownership and lifecycle restrictions.
/// - Tool allow-lists and execution limits.
/// - Human approval requirements.
/// - Safe handling of model-generated tool proposals.
/// - Appropriate audit and telemetry requirements.
///
/// The HTTP endpoints do not implement those
/// security controls themselves.
/// </remarks>
public interface IAuthorizedAgentApplicationService
{
    Task<Agent?> CreateAsync(
        AgentRequestIdentity identity,
        string name,
        string description,
        AgentExecutionLimits limits,
        CancellationToken cancellationToken = default);

    Task<Agent?> GetAsync(
        AgentRequestIdentity identity,
        Guid agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Agent>> ListAsync(
        AgentRequestIdentity identity,
        CancellationToken cancellationToken = default);

    Task<Agent?> UpdateMetadataAsync(
        AgentRequestIdentity identity,
        Guid agentId,
        string name,
        string description,
        CancellationToken cancellationToken = default);

    Task<Agent?> UpdateLimitsAsync(
        AgentRequestIdentity identity,
        Guid agentId,
        AgentExecutionLimits limits,
        CancellationToken cancellationToken = default);

    Task<Agent?> SetEnabledAsync(
        AgentRequestIdentity identity,
        Guid agentId,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<AgentExecutionResult?> ExecuteAsync(
        AgentRequestIdentity identity,
        Guid agentId,
        string userRequest,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines HTTP endpoints for tenant-scoped agents.
/// </summary>
/// <remarks>
/// Routes are not active until MapAgentEndpoints
/// is called from the API composition root.
///
/// An authenticated identity provider and authorized
/// application service must be registered before
/// these endpoints can be used.
/// </remarks>
public static class AgentEndpoints
{
    private const int MaximumNameLength = 200;

    private const int MaximumDescriptionLength = 2000;

    /// <summary>
    /// Maps the agent API routes.
    /// </summary>
    public static IEndpointRouteBuilder MapAgentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/api/agents")
            .WithTags("Agents");

        group.MapPost("/", CreateAgentAsync)
            .WithName("CreateAgent");

        group.MapGet("/", ListAgentsAsync)
            .WithName("ListAgents");

        group.MapGet("/{agentId:guid}", GetAgentAsync)
            .WithName("GetAgent");

        group.MapPut(
                "/{agentId:guid}/metadata",
                UpdateAgentMetadataAsync)
            .WithName("UpdateAgentMetadata");

        group.MapPut(
                "/{agentId:guid}/limits",
                UpdateAgentLimitsAsync)
            .WithName("UpdateAgentLimits");

        group.MapPost(
                "/{agentId:guid}/enable",
                EnableAgentAsync)
            .WithName("EnableAgent");

        group.MapPost(
                "/{agentId:guid}/disable",
                DisableAgentAsync)
            .WithName("DisableAgent");

        group.MapPost(
                "/{agentId:guid}/execute",
                ExecuteAgentAsync)
            .WithName("ExecuteAgent");

        return endpoints;
    }

    private static async Task<IResult> CreateAgentAsync(
        CreateAgentApiRequest request,
        HttpContext httpContext,
        IAgentRequestIdentityProvider identityProvider,
        IAuthorizedAgentApplicationService service,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            !IsValidRequiredText(
                request.Name,
                MaximumNameLength) ||
            !IsValidOptionalText(
                request.Description,
                MaximumDescriptionLength) ||
            !TryCreateLimits(
                request.MaximumReasoningSteps,
                request.MaximumToolExecutions,
                out var limits))
        {
            return Results.BadRequest();
        }

        var identity = await ResolveIdentityAsync(
            httpContext,
            identityProvider,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var agent = await service.CreateAsync(
            identity,
            request.Name,
            request.Description ?? string.Empty,
            limits!,
            cancellationToken);

        if (agent is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Created(
            $"/api/agents/{agent.Id}",
            ToResponse(agent));
    }

    private static async Task<IResult> GetAgentAsync(
        Guid agentId,
        HttpContext httpContext,
        IAgentRequestIdentityProvider identityProvider,
        IAuthorizedAgentApplicationService service,
        CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty)
        {
            return Results.BadRequest();
        }

        var identity = await ResolveIdentityAsync(
            httpContext,
            identityProvider,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var agent = await service.GetAsync(
            identity,
            agentId,
            cancellationToken);

        return agent is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(agent));
    }

    private static async Task<IResult> ListAgentsAsync(
        HttpContext httpContext,
        IAgentRequestIdentityProvider identityProvider,
        IAuthorizedAgentApplicationService service,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveIdentityAsync(
            httpContext,
            identityProvider,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var agents = await service.ListAsync(
            identity,
            cancellationToken);

        if (agents is null)
        {
            return Results.Problem(
                statusCode:
                    StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(
            agents.Select(ToResponse).ToArray());
    }

    private static async Task<IResult>
        UpdateAgentMetadataAsync(
            Guid agentId,
            UpdateAgentMetadataApiRequest request,
            HttpContext httpContext,
            IAgentRequestIdentityProvider identityProvider,
            IAuthorizedAgentApplicationService service,
            CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty ||
            request is null ||
            !IsValidRequiredText(
                request.Name,
                MaximumNameLength) ||
            !IsValidOptionalText(
                request.Description,
                MaximumDescriptionLength))
        {
            return Results.BadRequest();
        }

        var identity = await ResolveIdentityAsync(
            httpContext,
            identityProvider,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var agent = await service.UpdateMetadataAsync(
            identity,
            agentId,
            request.Name,
            request.Description ?? string.Empty,
            cancellationToken);

        return agent is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(agent));
    }

    private static async Task<IResult>
        UpdateAgentLimitsAsync(
            Guid agentId,
            UpdateAgentLimitsApiRequest request,
            HttpContext httpContext,
            IAgentRequestIdentityProvider identityProvider,
            IAuthorizedAgentApplicationService service,
            CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty ||
            request is null ||
            !TryCreateLimits(
                request.MaximumReasoningSteps,
                request.MaximumToolExecutions,
                out var limits))
        {
            return Results.BadRequest();
        }

        var identity = await ResolveIdentityAsync(
            httpContext,
            identityProvider,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var agent = await service.UpdateLimitsAsync(
            identity,
            agentId,
            limits!,
            cancellationToken);

        return agent is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(agent));
    }

    private static Task<IResult> EnableAgentAsync(
        Guid agentId,
        HttpContext httpContext,
        IAgentRequestIdentityProvider identityProvider,
        IAuthorizedAgentApplicationService service,
        CancellationToken cancellationToken)
    {
        return SetAgentEnabledAsync(
            agentId,
            enabled: true,
            httpContext,
            identityProvider,
            service,
            cancellationToken);
    }

    private static Task<IResult> DisableAgentAsync(
        Guid agentId,
        HttpContext httpContext,
        IAgentRequestIdentityProvider identityProvider,
        IAuthorizedAgentApplicationService service,
        CancellationToken cancellationToken)
    {
        return SetAgentEnabledAsync(
            agentId,
            enabled: false,
            httpContext,
            identityProvider,
            service,
            cancellationToken);
    }

    private static async Task<IResult>
        SetAgentEnabledAsync(
            Guid agentId,
            bool enabled,
            HttpContext httpContext,
            IAgentRequestIdentityProvider identityProvider,
            IAuthorizedAgentApplicationService service,
            CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty)
        {
            return Results.BadRequest();
        }

        var identity = await ResolveIdentityAsync(
            httpContext,
            identityProvider,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var agent = await service.SetEnabledAsync(
            identity,
            agentId,
            enabled,
            cancellationToken);

        return agent is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(agent));
    }

    private static async Task<IResult> ExecuteAgentAsync(
        Guid agentId,
        ExecuteAgentApiRequest request,
        HttpContext httpContext,
        IAgentRequestIdentityProvider identityProvider,
        IAuthorizedAgentApplicationService service,
        CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty ||
            request is null ||
            !IsValidRequiredText(
                request.UserRequest,
                AgentReasoningValidation
                    .MaximumUserRequestCharacters))
        {
            return Results.BadRequest();
        }

        var identity = await ResolveIdentityAsync(
            httpContext,
            identityProvider,
            cancellationToken);

        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var result = await service.ExecuteAsync(
            identity,
            agentId,
            request.UserRequest,
            cancellationToken);

        if (result is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(
            new AgentExecutionApiResponse(
                AgentId: result.AgentId,
                Status: result.Status,
                FinalAnswer: result.FinalAnswer,
                ReasoningSteps: result.ReasoningSteps,
                ToolExecutions: result.ToolExecutions));
    }

    private static async Task<AgentRequestIdentity?>
        ResolveIdentityAsync(
            HttpContext httpContext,
            IAgentRequestIdentityProvider identityProvider,
            CancellationToken cancellationToken)
    {
        var identity = await identityProvider.GetIdentityAsync(
            httpContext,
            cancellationToken);

        if (identity is null ||
            string.IsNullOrWhiteSpace(identity.TenantId) ||
            string.IsNullOrWhiteSpace(identity.SubjectId))
        {
            return null;
        }

        return identity;
    }

    private static AgentApiResponse ToResponse(
        Agent agent)
    {
        return new AgentApiResponse(
            Id: agent.Id,
            Name: agent.Name,
            Description: agent.Description,
            Status: agent.Status,
            MaximumReasoningSteps:
                agent.ExecutionLimits.MaximumReasoningSteps,
            MaximumToolExecutions:
                agent.ExecutionLimits.MaximumToolExecutions,
            CreatedAtUtc: agent.CreatedAtUtc,
            UpdatedAtUtc: agent.UpdatedAtUtc);
    }

    private static bool TryCreateLimits(
        int maximumReasoningSteps,
        int maximumToolExecutions,
        out AgentExecutionLimits? limits)
    {
        limits = null;

        if (maximumReasoningSteps <= 0 ||
            maximumReasoningSteps >
                AgentExecutionLimits
                    .MaximumAllowedReasoningSteps ||
            maximumToolExecutions < 0 ||
            maximumToolExecutions >
                AgentExecutionLimits
                    .MaximumAllowedToolExecutions)
        {
            return false;
        }

        limits = new AgentExecutionLimits
        {
            MaximumReasoningSteps = maximumReasoningSteps,
            MaximumToolExecutions = maximumToolExecutions
        };

        return true;
    }

    private static bool IsValidRequiredText(
        string? value,
        int maximumLength)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Trim().Length <= maximumLength;
    }

    private static bool IsValidOptionalText(
        string? value,
        int maximumLength)
    {
        return value is null ||
               value.Trim().Length <= maximumLength;
    }
}