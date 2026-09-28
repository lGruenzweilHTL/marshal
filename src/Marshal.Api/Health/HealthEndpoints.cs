using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Marshal.Api.Health;

/// <summary>
/// <c>/health</c>: liveness and readiness (database, configuration, secrets, MCP servers, memory vector search), unauthenticated, without values.
/// </summary>
internal static class HealthEndpoints
{
    public static IHealthChecksBuilder AddMarshalHealthChecks(this IServiceCollection services) =>
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<ConfigurationHealthCheck>("config")
            .AddCheck<SecretsHealthCheck>("secrets")
            .AddCheck<McpHealthCheck>("mcp")
            .AddCheck<MemoryHealthCheck>("memory")
            .AddCheck<HomeAssistantHealthCheck>("homeassistant");

    public static IEndpointRouteBuilder MapMarshalHealth(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
            ResponseWriter = WriteAsync,
        }).AllowAnonymous();
        return app;
    }

    private static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var body = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.ToDictionary(entry => entry.Key, entry => new
            {
                status = entry.Value.Status.ToString().ToLowerInvariant(),
                description = entry.Value.Description,
                warnings = entry.Value.Data.TryGetValue("warnings", out object? warnings) ? warnings : null,
            }),
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonSerializerOptions.Web), context.RequestAborted);
    }
}
