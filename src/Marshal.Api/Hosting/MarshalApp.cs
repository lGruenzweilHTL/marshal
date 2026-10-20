using System.Collections;
using Marshal.Api.Apps;
using Marshal.Api.Approvals;
using Marshal.Api.AiTasks;
using Marshal.Api.Artifacts;
using Marshal.Api.Auth;
using Marshal.Api.Configuration;
using Marshal.Api.Conversation;
using Marshal.Api.Displays;
using Marshal.Api.Executors;
using Marshal.Api.Health;
using Marshal.Api.Hooks;
using Marshal.Api.LiveEvents;
using Marshal.Api.Mail;
using Marshal.Api.McpServer;
using Marshal.Api.Notifications;
using Marshal.Api.Memory;
using Marshal.Api.Music;
using Marshal.Api.Printers;
using Marshal.Api.Proposals;
using Marshal.Api.Runs;
using Marshal.Api.Security;
using Marshal.Api.Tasks;
using Marshal.Api.Versioning;
using Marshal.Artifacts;
using Marshal.Core;
using Marshal.Core.Configuration;
using Marshal.Data;
using Marshal.Demo;
using Marshal.Integrations.FreshRss;
using Marshal.Integrations.GitHub;
using Marshal.Integrations.HomeAssistant;
using Marshal.Integrations.Mail;
using Marshal.Integrations.Media;
using Marshal.Integrations.Moonraker;
using Marshal.Integrations.Notes;
using Marshal.Integrations.Proxmox;
using Marshal.Memory;
using Marshal.Tasks;
using Microsoft.AspNetCore.HttpOverrides;
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
        builder.Services.AddMarshalHomeAssistant(builder.Configuration);
        builder.Services.AddMarshalMemory(builder.Configuration);
        builder.Services.AddMarshalPrinters(builder.Configuration, SnapshotDirectory);
        builder.Services.AddMarshalMail(builder.Configuration);
        builder.Services.AddMarshalFreshRss(builder.Configuration);
        builder.Services.AddMarshalGitHub(builder.Configuration);
        builder.Services.AddMarshalProxmox(builder.Configuration);
        builder.Services.AddMarshalMedia(builder.Configuration);
        builder.Services.AddMarshalNotes(builder.Configuration, NotesDirectory);
        builder.Services.AddMarshalArtifacts(builder.Configuration, ArtifactDirectory);
        builder.Services.AddMarshalTasks(builder.Configuration);
        builder.Services.AddMarshalData(builder.Configuration, ConnectionString);
        builder.Services.AddMarshalApi(builder.Configuration);
        // Last: demo mode replaces the registrations of unconfigured integrations.
        builder.Services.AddMarshalDemo(builder.Configuration);
        builder.Services.AddSingleton(new ProcessEnvironment(environment));

        WebApplication app = builder.Build();
        HttpOptions http = app.Services.GetRequiredService<IOptions<HttpOptions>>().Value;
        if (http.TrustForwardedHeaders)
        {
            var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost };
            forwarded.KnownIPNetworks.Clear();
            forwarded.KnownProxies.Clear();
            app.UseForwardedHeaders(forwarded);
        }

        // Unhandled exceptions become a `server.unexpected` problem (UnexpectedProblems) and are logged with the trace id.
        app.UseExceptionHandler();
        app.UseSecurityHeaders();
        string? pathBase = http.PathBase;
        if (!string.IsNullOrWhiteSpace(pathBase))
            app.UsePathBase(pathBase);

        app.UseSpaAssets();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseMiddleware<AntiforgeryValidationMiddleware>();
        app.UseMiddleware<OwnerContextMiddleware>();

        app.MapMarshalHealth();
        app.MapVersionDiscovery();
        app.MapOpenApi().WithDocumentPerVersion().AllowAnonymous();

        Asp.Versioning.Builder.IVersionedEndpointRouteBuilder api = app.NewVersionedApi("Marshal");
        RouteGroupBuilder v1 = api.MapGroup("/api/v{version:apiVersion}").HasApiVersion(ApiVersions.V1);
        v1.MapAuthEndpoints();
        v1.MapLocalAuthEndpoints();
        v1.MapMeEndpoints();
        v1.MapTokenEndpoints();
        v1.MapSecretEndpoints();
        v1.MapPreferencesEndpoints();
        v1.MapLiveEventEndpoints();
        v1.MapRunEndpoints();
        v1.MapSystemEndpoints();
        v1.MapConfigEndpoints();
        v1.MapMemoryEndpoints();
        v1.MapPrinterEndpoints();
        v1.MapMusicEndpoints();
        v1.MapAppEndpoints();
        v1.MapMailAccountEndpoints();
        v1.MapConversationEndpoints();
        v1.MapConversationHistoryEndpoints();
        v1.MapApprovalEndpoints();
        v1.MapTaskEndpoints();
        v1.MapNotificationEndpoints();
        v1.MapProposalEndpoints();
        v1.MapExecutorCallbackEndpoints();
        v1.MapArtifactEndpoints();
        v1.MapAiTaskEndpoints();
        v1.MapDisplayEndpoints();
        v1.MapHookEndpoints();
        app.MapMailOAuthCallback();
        app.MapMarshalMcp();
        app.MapMcpAppHtml();
        app.MapSpaFallback();
        return app;
    }

    internal static string SnapshotDirectory(IServiceProvider services) =>
        Path.Combine(services.GetRequiredService<IOptions<PathsOptions>>().Value.Data, "snapshots");

    internal static string ArtifactDirectory(IServiceProvider services) =>
        Path.Combine(services.GetRequiredService<IOptions<PathsOptions>>().Value.Data, "artifacts");

    internal static string NotesDirectory(IServiceProvider services) =>
        Path.Combine(services.GetRequiredService<IOptions<PathsOptions>>().Value.Data, "notes");

    internal static string ConnectionString(IServiceProvider services) =>
        $"Data Source={Path.Combine(services.GetRequiredService<IOptions<PathsOptions>>().Value.Data, "marshal.db")}";
}
