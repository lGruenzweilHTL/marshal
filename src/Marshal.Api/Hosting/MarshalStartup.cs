using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Api.Configuration;
using Marshal.Core.Configuration;
using Marshal.Core.Mcp;
using Marshal.Core.Secrets;
using Marshal.Data;
using Marshal.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Hosting;

/// <summary>
/// Runs before the server starts, after the configuration stage passed (<see cref="ConfigurationStage"/>): migrates
/// the database (after copying it), fails startup listing every unresolved required secret, then starts MCP servers
/// and loads the tool registry.
/// </summary>
internal sealed partial class MarshalStartup(
    IOptions<PathsOptions> paths,
    IOptions<OwnerOptions> owner,
    IInstanceTimeZone instanceTimeZone,
    IDbContextFactory<MarshalDbContext> contexts,
    SecretBinder secretBinder,
    SecretHealth secretHealth,
    IToolRegistry registry,
    IServiceProvider services,
    IConfiguration configuration,
    ProcessEnvironment environment,
    TimeProvider timeProvider,
    ILoggerFactory loggers) : IHostedLifecycleService
{
    // Startup waits this long for MCP servers' first connection; slower ones join the registry later.
    private static readonly TimeSpan _mcpStartupWait = TimeSpan.FromSeconds(10);

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.Value.Data);
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            var initializer = new DatabaseInitializer(db, timeProvider, loggers.CreateLogger<DatabaseInitializer>());
            await initializer.InitializeAsync(new InstanceOwnerSeed(owner.Value.DisplayName, owner.Value.Language, await instanceTimeZone.GetAsync(cancellationToken)), cancellationToken);
        }

        // Secrets can live in the database, so they resolve only after it is migrated.
        await BindSecretsAsync(cancellationToken);
        await StartMcpServersAsync(cancellationToken);
        ToolSnapshot tools = await registry.RefreshAsync(cancellationToken);
        foreach (ToolSourceError error in tools.Errors)
            LogToolSourceRejected(loggers.CreateLogger<MarshalStartup>(), error.Source, error.Message);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task BindSecretsAsync(CancellationToken cancellationToken)
    {
        var reports = new List<SecretBindingReport>();
        foreach ((object options, string path) in SecretBoundOptions.All(services))
            reports.Add(await secretBinder.BindAsync(options, path, cancellationToken));

        StartupError[] missing = [.. reports
            .SelectMany(report => report.MissingRequired)
            .Select(reference => ConfigurationErrors.Locate($"{reference} does not resolve.", environment.Variables))];
        if (missing.Length > 0)
            throw new StartupFailedException(missing, StartupReport.IsVerbose(configuration));

        secretHealth.Set([.. reports.SelectMany(report => report.MissingOptional)]);
    }

    // Invalid server files are skipped (the others load); a literal secret in one already failed the configuration stage.
    private async Task StartMcpServersAsync(CancellationToken cancellationToken)
    {
        ILogger logger = loggers.CreateLogger<MarshalStartup>();
        foreach (ConfigFileError error in services.GetRequiredService<IMcpServerCatalog>().Errors)
            LogMcpServerRejected(logger, error.File, error.Message);

        await services.GetRequiredService<McpToolSources>().StartAsync(_mcpStartupWait, cancellationToken);
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool source {Source} rejected: {Reason}")]
    private static partial void LogToolSourceRejected(ILogger logger, string source, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP server file {File} rejected: {Reason}")]
    private static partial void LogMcpServerRejected(ILogger logger, string file, string reason);
}
