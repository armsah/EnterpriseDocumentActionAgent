using EnterpriseDocumentActionAgent.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace EnterpriseDocumentActionAgent.Api.Security;

/// <summary>
/// Provides a fixed request identity for local
/// development and diagnostic testing.
/// </summary>
/// <remarks>
/// WARNING: This provider does not authenticate users.
///
/// It must only be registered in the Development
/// environment and must never be used as a
/// production authentication mechanism.
///
/// Tenant and subject identifiers are configured
/// server-side and are not obtained from HTTP headers,
/// query strings, or request bodies.
/// </remarks>
public sealed class DevelopmentRequestIdentity
    : IGroundedQuestionRequestIdentityProvider
{
    private const string DevelopmentTenantId =
        "development-tenant";

    private const string DevelopmentSubjectId =
        "development-user";

    private readonly IHostEnvironment _environment;

    public DevelopmentRequestIdentity(
        IHostEnvironment environment)
    {
        _environment = environment
            ?? throw new ArgumentNullException(
                nameof(environment));

        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "DevelopmentRequestIdentity may only " +
                "be constructed in the Development " +
                "environment.");
        }
    }

    /// <summary>
    /// Returns the fixed development identity.
    /// </summary>
    /// <remarks>
    /// The HTTP context is accepted to satisfy the
    /// application identity provider contract.
    ///
    /// Client-supplied identity values are ignored.
    /// </remarks>
    public Task<GroundedQuestionRequestIdentity?> GetIdentityAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Development request identity is " +
                "not available outside Development.");
        }

        var identity = new GroundedQuestionRequestIdentity(
            TenantId: DevelopmentTenantId,
            SubjectId: DevelopmentSubjectId);

        return Task.FromResult<GroundedQuestionRequestIdentity?>(identity);
    }
}