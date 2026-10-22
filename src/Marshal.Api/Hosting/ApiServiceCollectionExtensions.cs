using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Marshal.Abstractions;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Printers;
using Marshal.Api.Auth;
using Marshal.Api.Health;
using Marshal.Api.Hooks;
using Marshal.Api.Mail;
using Marshal.Api.McpServer;
using Marshal.Api.Models;
using Marshal.Api.Printers;
using Marshal.Api.Problems;
using Marshal.Api.Versioning;
using Marshal.Core.Configuration;
using Marshal.Core.Mcp;
using Marshal.Core.Models;
using Marshal.Core.Recording;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenTelemetry;
using OpenTelemetry.Resources;

namespace Marshal.Api.Hosting;

/// <summary>
/// Registers the HTTP host: versioned API, OpenAPI, token authentication, health, providers, startup checks.
/// </summary>
public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HttpOptions>().Bind(configuration.GetSection("http"));
        services.AddHookRateLimiter();
        services.AddOptions<OwnerOptions>().Bind(configuration.GetSection("owner")).ValidateAttributes().ValidateOnStart();

        services.AddSingleton<ApiSnapshotLinks>();
        services.AddSingleton<ISnapshotLinks>(provider => provider.GetRequiredService<ApiSnapshotLinks>());
        services.AddSingleton<Artifacts.ApiArtifactLinks>();
        services.AddSingleton<Abstractions.Artifacts.IArtifactLinks>(provider => provider.GetRequiredService<Artifacts.ApiArtifactLinks>());
        services.AddSingleton<Displays.ApiDisplayLinks>();
        services.AddSingleton<Abstractions.Artifacts.IDisplayLinks>(provider => provider.GetRequiredService<Displays.ApiDisplayLinks>());
        services.AddSingleton<IValidateOptions<Core.Displays.DisplayOptions>, Displays.DisplayBaseUrlValidator>();
        services.AddSingleton<PrinterControlConfirmations>();
        services.AddSingleton<MailOAuthFlows>();
        services.AddSingleton(new LiveTopic(PrinterLiveEvents.Topic));
        services.AddHostedService<PrinterLiveEvents>();

        services.AddSingleton<IChatClientFactory, ProviderChatClientFactory>();
        services.AddSingleton<IEmbeddingGeneratorFactory, ProviderEmbeddingGeneratorFactory>();
        services.AddDataProtection().SetApplicationName("Marshal");
        services.AddOptions<KeyManagementOptions>().Configure<IOptions<PathsOptions>, ILoggerFactory>((options, paths, loggers) =>
            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(paths.Value.Data, "keys")), loggers));

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            // Numbers are JSON numbers only, so OpenAPI (and the generated SPA types) describe them as `number`.
            options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
        });
        services.AddProblemDetails(options => options.CustomizeProblemDetails = UnexpectedProblems.Customize);
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
                .AddOperationTransformer<OpenApiRouteSummaries>()
                .AddDocumentTransformer<OpenApiSecurity>()
                .AddOperationTransformer<OpenApiSecurity>()
                .AddSchemaTransformer((schema, context, _) =>
                {
                    // Typed ids serialize as their Guid (GuidIdJsonConverter); the generator cannot see through the converter.
                    if (IsGuidId(context.JsonTypeInfo.Type))
                    {
                        schema.Type = JsonSchemaType.String;
                        schema.Format = "uuid";
                    }
                    else if ((Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type) == typeof(ChunkId))
                    {
                        // Chunk ids are 26-character base32 hashes (ChunkIdJsonConverter).
                        schema.Type = JsonSchemaType.String;
                        schema.MinLength = ChunkId.Length;
                        schema.MaxLength = ChunkId.Length;
                    }

                    return Task.CompletedTask;
                }));

        services.AddMarshalAuth(configuration);
        services.AddAuthorization(options =>
        {
            options.AddScopePolicies();
            options.AddMcpPolicy();
        });
        services.AddMarshalMcpServer();
        services.AddSingleton<Marshal.Api.Widgets.WidgetRuns>();

        services.AddMarshalHealthChecks();
        services.AddHostedService<MarshalStartup>();

        OpenTelemetryBuilder telemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddAttributes([new("marshal.genai_semconv_version", GenAiConventions.Version)]))
            .WithTracing(tracing => tracing.AddSource(RunTracing.SourceName, ModelTelemetry.SourceName, McpToolSource.TracingSourceName))
            .WithMetrics(metrics => metrics.AddMeter(OpenTelemetryRunObserver.MeterName, ModelTelemetry.SourceName));
        if (!string.IsNullOrWhiteSpace(configuration["telemetry:otlpEndpoint"]))
            telemetry.UseOtlpExporter(OpenTelemetry.Exporter.OtlpExportProtocol.Grpc, new Uri(configuration["telemetry:otlpEndpoint"]!));

        return services;
    }

    private static bool IsGuidId(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type).GetInterfaces().Any(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IGuidId<>));
}
