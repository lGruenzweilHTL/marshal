using System.Collections;
using Marshal.Api.Auth;
using Marshal.Api.Configuration;
using Marshal.Api.Conversation;
using Marshal.Api.Health;
using Marshal.Api.Versioning;
using Marshal.Core;
using Marshal.Core.Configuration;
using Marshal.Data;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Hosting;

/// <summary>
/// Composition root of the web host.
/// </summary>
public static class MarshalApp
{
    /// <summary>Environment variable holding the config directory (read before configuration exists).</summary>
    public const string ConfigDirectoryVariable = "MARSHAL__Paths__Config";

    public static WebApplication Create(string[] args, IDictionary environment)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddMarshalConfiguration(environment[ConfigDirectoryVariable] as string ?? "/config", environment);
        builder.WebHost.UseUrls(builder.Configuration["http:urls"] ?? new HttpOptions().Urls);

        builder.Services.AddMarshalCore(builder.Configuration, environment);
        builder.Services.AddMarshalData(builder.Configuration, ConnectionString);
        builder.Services.AddMarshalApi(builder.Configuration);

        WebApplication app = builder.Build();
        string? pathBase = app.Services.GetRequiredService<IOptions<HttpOptions>>().Value.PathBase;
        if (!string.IsNullOrWhiteSpace(pathBase))
            app.UsePathBase(pathBase);

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapMarshalHealth();
        app.MapVersionDiscovery();
        app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();

        Asp.Versioning.Builder.IVersionedEndpointRouteBuilder api = app.NewVersionedApi("Marshal");
        RouteGroupBuilder v1 = api.MapGroup("/api/v{version:apiVersion}").HasApiVersion(ApiVersions.V1);
        v1.MapAuthEndpoints();
        v1.MapLocalAuthEndpoints();
        v1.MapConversationEndpoints();
        return app;
    }

    internal static string ConnectionString(IServiceProvider services) =>
        $"Data Source={Path.Combine(services.GetRequiredService<IOptions<PathsOptions>>().Value.Data, "marshal.db")}";
}
