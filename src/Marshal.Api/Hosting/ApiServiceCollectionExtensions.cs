using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Marshal.Abstractions;
using Marshal.Api.Auth;
using Marshal.Api.Health;
using Marshal.Api.Models;
using Marshal.Api.Versioning;
using Marshal.Core.Configuration;
using Marshal.Core.Models;
using Marshal.Core.Recording;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenTelemetry;

namespace Marshal.Api.Hosting;

/// <summary>
/// Registers the HTTP host: versioned API, OpenAPI, token authentication, health, providers, startup checks.
/// </summary>
public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HttpOptions>().Bind(configuration.GetSection("http"));
        services.AddOptions<OwnerOptions>().Bind(configuration.GetSection("owner")).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton<IChatClientFactory, ProviderChatClientFactory>();
        services.AddDataProtection().SetApplicationName("Marshal");
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<PathsOptions>, ILoggerFactory>((options, paths, loggers) =>
            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(paths.Value.Data, "keys")), loggers));

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            // Numbers are JSON numbers only, so OpenAPI (and the generated SPA types) describe them as `number`.
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.AddProblemDetails();
        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = ApiVersions.V1;
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            })
            // A fixed title: the default is the host assembly name, which differs between the app and test hosts.
            .AddOpenApi(options => options.Document
                .AddDocumentTransformer((document, context, _) =>
                {
                    document.Info.Title = $"Marshal API | {context.DocumentName}";
                    return Task.CompletedTask;
                })
                .AddSchemaTransformer((schema, context, _) =>
                {
                    // Typed ids serialize as their Guid (GuidIdJsonConverter); the generator cannot see through the converter.
                    if (IsGuidId(context.JsonTypeInfo.Type))
                    {
                        schema.Type = JsonSchemaType.String;
                        schema.Format = "uuid";
                    }

                    return Task.CompletedTask;
                }));

        services.AddMarshalAuth(configuration);
        services.AddAuthorization(options => options.AddScopePolicies());

        services.AddMarshalHealthChecks();
        services.AddHostedService<MarshalStartup>();

        OpenTelemetryBuilder telemetry = services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(OpenTelemetryRunObserver.SourceName))
            .WithMetrics(metrics => metrics.AddMeter(OpenTelemetryRunObserver.MeterName));
        if (!string.IsNullOrWhiteSpace(configuration["telemetry:otlpEndpoint"]))
            telemetry.UseOtlpExporter(OpenTelemetry.Exporter.OtlpExportProtocol.Grpc, new Uri(configuration["telemetry:otlpEndpoint"]!));

        return services;
    }

    private static bool IsGuidId(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type).GetInterfaces().Any(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IGuidId<>));
}
