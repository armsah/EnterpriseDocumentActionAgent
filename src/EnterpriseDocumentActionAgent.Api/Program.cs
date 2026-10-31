using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------
// 1. Register application services
// ------------------------------------------------------------

// Standardized API error responses.
builder.Services.AddProblemDetails();

// Health checks for application monitoring.
builder.Services.AddHealthChecks();

// OpenAPI endpoint discovery.
// Swagger UI can be configured separately.
builder.Services.AddEndpointsApiExplorer();

// ------------------------------------------------------------
// 2. Build the application
// ------------------------------------------------------------

var app = builder.Build();

// ------------------------------------------------------------
// 3. Configure the HTTP request pipeline
// ------------------------------------------------------------

// Handle unexpected exceptions with standardized responses.
app.UseExceptionHandler();

// ------------------------------------------------------------
// 4. Application information endpoint
// ------------------------------------------------------------

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        Application = "Enterprise Document Action Agent",
        Version = "1.0.0",
        Framework = ".NET 8",
        Status = "Running",
        TimestampUtc = DateTimeOffset.UtcNow
    });
})
.WithName("GetApplicationInformation")
.WithTags("System");

// ------------------------------------------------------------
// 5. Health-check endpoint
// ------------------------------------------------------------

app.MapHealthChecks("/health")
   .WithName("GetApplicationHealth")
   .WithTags("System");

// ------------------------------------------------------------
// 6. Readiness endpoint
// ------------------------------------------------------------

// For now, readiness confirms that the API process is running.
// Future implementations will check PostgreSQL and other
// required dependencies before reporting readiness.

app.MapGet("/ready", () =>
{
    return Results.Ok(new
    {
        Status = HealthStatus.Healthy.ToString(),
        TimestampUtc = DateTimeOffset.UtcNow
    });
})
.WithName("GetApplicationReadiness")
.WithTags("System");

// ------------------------------------------------------------
// 7. Start the application
// ------------------------------------------------------------

app.Run();