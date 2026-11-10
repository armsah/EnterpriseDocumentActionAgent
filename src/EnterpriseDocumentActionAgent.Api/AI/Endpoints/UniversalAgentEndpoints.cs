using System.Diagnostics;

using EnterpriseDocumentActionAgent.Application.AI.Messages;
using EnterpriseDocumentActionAgent.Application.AI.Providers;
using EnterpriseDocumentActionAgent.Application.AI.Runtime;

using EnterpriseDocumentActionAgent.Infrastructure.AI.Providers;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseDocumentActionAgent.Api.AI.Endpoints;

/// <summary>
/// Represents an HTTP request to execute an AI agent.
/// </summary>
public sealed record UniversalAgentExecuteRequest
{
    public string AgentId { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public string? ProviderId { get; init; }

    public string? SystemInstructions { get; init; }

    public double Temperature { get; init; } = 0.7;

    public int MaxOutputTokens { get; init; } = 2048;
}

/// <summary>
/// Represents an HTTP response from an AI agent.
/// </summary>
public sealed record UniversalAgentExecuteResponse
{
    public string AgentId { get; init; } = string.Empty;

    public Guid ConversationId { get; init; }

    public string ProviderId { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public string FinishReason { get; init; } = string.Empty;

    public double DurationMilliseconds { get; init; }
}

/// <summary>
/// Describes an AI provider available to the framework.
/// </summary>
public sealed record UniversalAgentProviderResponse
{
    public string ProviderId { get; init; } = string.Empty;

    public bool IsRouter { get; init; }
}

/// <summary>
/// Describes the selected route for an AI model.
/// </summary>
public sealed record UniversalAgentRouteResponse
{
    public string Model { get; init; } = string.Empty;

    public string ProviderId { get; init; } = string.Empty;

    public bool IsExplicit { get; init; }
}

/// <summary>
/// Registers minimal API endpoints for the universal
/// AI agent framework.
/// </summary>
/// <remarks>
/// Authentication, authorization, tenant isolation,
/// and rate limiting must be configured by the host.
///
/// These endpoints are not automatically mapped.
/// Call MapUniversalAgentEndpoints during API startup.
/// </remarks>
public static class UniversalAgentEndpoints
{
    public const string DefaultRoutePrefix = "/api/agents";

    /// <summary>
    /// Maps the universal agent HTTP endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapUniversalAgentEndpoints(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = DefaultRoutePrefix)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        ArgumentException.ThrowIfNullOrWhiteSpace(routePrefix);

        var group = endpoints.MapGroup(
            routePrefix.TrimEnd('/'));

        group.WithTags("Universal AI Agents");

        group.MapPost(
            "/execute",
            ExecuteAsync)
            .WithName("UniversalAgentExecute")
            .WithSummary("Execute an AI agent request.")
            .Produces<UniversalAgentExecuteResponse>(
                StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(
                StatusCodes.Status503ServiceUnavailable);

        group.MapGet(
            "/providers",
            GetProviders)
            .WithName("UniversalAgentProviders")
            .WithSummary("List registered AI providers.")
            .Produces<
                IReadOnlyList<UniversalAgentProviderResponse>>(
                StatusCodes.Status200OK);

        group.MapGet(
            "/routes/{model}",
            GetRoute)
            .WithName("UniversalAgentRoute")
            .WithSummary("Resolve the provider for a model.")
            .Produces<UniversalAgentRouteResponse>(
                StatusCodes.Status200OK)
            .ProducesProblem(
                StatusCodes.Status404NotFound);

        group.MapGet(
            "/ready",
            GetReadiness)
            .WithName("UniversalAgentReadiness")
            .WithSummary("Inspect AI framework readiness.")
            .Produces(
                StatusCodes.Status200OK)
            .ProducesProblem(
                StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    /// <summary>
    /// Executes a single AI model request.
    /// </summary>
    private static async Task<IResult> ExecuteAsync(
        UniversalAgentExecuteRequest request,
        AiProviderRegistry registry,
        AgentExecutionTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["request"] =
                    [
                        "A request body is required."
                    ]
                });
        }

        var errors = ValidateRequest(request);

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var configuration = new AgentConfiguration(
            agentId: request.AgentId.Trim(),
            model: request.Model.Trim(),
            systemInstructions:
                request.SystemInstructions?.Trim()
                ?? string.Empty,
            providerId: request.ProviderId,
            temperature: request.Temperature,
            maxOutputTokens: request.MaxOutputTokens);

        var runtime = new AgentRuntime(registry);

        var conversationId = Guid.NewGuid();

        var userMessage = AiMessage.User(
            conversationId,
            request.Prompt);

        var messages = new List<AiMessage>();

        if (!string.IsNullOrWhiteSpace(
            configuration.SystemInstructions))
        {
            messages.Add(
                AiMessage.System(
                    conversationId,
                    configuration.SystemInstructions));
        }

        messages.Add(userMessage);

        var modelRequest = new AiModelRequest(
            conversationId: conversationId,
            model: configuration.Model,
            messages: messages,
            temperature: configuration.Temperature,
            maxOutputTokens: configuration.MaxOutputTokens);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var response =
                await telemetry.TrackExecutionAsync(
                    configuration.AgentId,
                    token => runtime.ExecuteAsync(
                        modelRequest,
                        cancellationToken: token),
                    model: configuration.Model,
                    providerId: configuration.ProviderId,
                    cancellationToken: cancellationToken);

            stopwatch.Stop();

            return Results.Ok(
                new UniversalAgentExecuteResponse
                {
                    AgentId = configuration.AgentId,
                    ConversationId = response.ConversationId,
                    ProviderId = response.Provider,
                    Model = response.Model,
                    Text = response.Text,
                    FinishReason =
                        response.FinishReason.ToString(),
                    DurationMilliseconds =
                        stopwatch.Elapsed.TotalMilliseconds
                });
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(
                StatusCodes.Status499ClientClosedRequest);
        }
        catch (InvalidOperationException)
        {
            return Results.Problem(
                title: "Agent execution unavailable.",
                detail:
                    "The requested model or provider " +
                    "is not available.",
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Lists registered model providers.
    /// </summary>
    private static IResult GetProviders(
        AiProviderRegistry registry)
    {
        var providers = registry.GetProviderIds()
            .Select(providerId =>
                new UniversalAgentProviderResponse
                {
                    ProviderId = providerId,
                    IsRouter =
                        string.Equals(
                            providerId,
                            AiProviderRouter.DefaultProviderId,
                            StringComparison.OrdinalIgnoreCase)
                })
            .ToArray();

        return Results.Ok(providers);
    }

    /// <summary>
    /// Resolves a model route.
    /// </summary>
    private static IResult GetRoute(
        string model,
        AiProviderRouter router)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return Results.BadRequest();
        }

        try
        {
            var route = router.ResolveRoute(model);

            return Results.Ok(
                new UniversalAgentRouteResponse
                {
                    Model = route.Model,
                    ProviderId = route.ProviderId,
                    IsExplicit = route.IsExplicit
                });
        }
        catch (InvalidOperationException)
        {
            return Results.Problem(
                title: "No provider route found.",
                statusCode:
                    StatusCodes.Status404NotFound);
        }
    }

    /// <summary>
    /// Reports whether the framework has
    /// registered model providers.
    /// </summary>
    private static IResult GetReadiness(
        AiProviderRegistry registry)
    {
        var providerCount =
            registry.GetProviderIds().Count;

        if (providerCount == 0)
        {
            return Results.Problem(
                title: "No AI model providers registered.",
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(
            new
            {
                Status = "ready",
                ProviderCount = providerCount
            });
    }

    /// <summary>
    /// Validates execution request fields.
    /// </summary>
    private static Dictionary<string, string[]>
        ValidateRequest(
            UniversalAgentExecuteRequest request)
    {
        var errors =
            new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.AgentId))
        {
            errors[nameof(request.AgentId)] =
            [
                "AgentId is required."
            ];
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            errors[nameof(request.Model)] =
            [
                "Model is required."
            ];
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            errors[nameof(request.Prompt)] =
            [
                "Prompt is required."
            ];
        }

        if (request.Temperature < 0 ||
            request.Temperature > 2 ||
            double.IsNaN(request.Temperature) ||
            double.IsInfinity(request.Temperature))
        {
            errors[nameof(request.Temperature)] =
            [
                "Temperature must be between 0 and 2."
            ];
        }

        if (request.MaxOutputTokens < 1)
        {
            errors[nameof(request.MaxOutputTokens)] =
            [
                "MaxOutputTokens must be positive."
            ];
        }

        return errors;
    }
}