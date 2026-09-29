using Marshal.Abstractions;
using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Api.Auth;
using Marshal.Api.Configuration;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Marshal.Core.Mcp;
using Marshal.Core.Models;
using Marshal.Core.Satellites;
using Marshal.Core.Secrets;
using Marshal.Data;
using Marshal.Data.Persistence;
using Marshal.Integrations.HomeAssistant;
using Marshal.Integrations.Moonraker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Hosting;

/// <summary>
/// Runs before the server starts and fails startup when anything is unsafe or incomplete: literal secrets in
/// <c>marshal.yaml</c>, invalid hot-reloaded files, unresolved required secrets. Also migrates the database
/// (after copying it) and loads the tool registry.
/// </summary>
internal sealed partial class MarshalStartup(
    IOptions<PathsOptions> paths,
    IOptions<OwnerOptions> owner,
    IInstanceTimeZone instanceTimeZone,
    IOptions<ModelRoutingOptions> models,
    IOptions<AuthOptions> auth,
    IOptions<HomeAssistantOptions> homeAssistant,
    IOptions<PrinterOptions> printers,
    IConfigSecretScanner scanner,
    IDbContextFactory<MarshalDbContext> contexts,
    SecretBinder secretBinder,
    SecretHealth secretHealth,
    IToolRegistry registry,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILoggerFactory loggers) : IHostedLifecycleService
{
    // Startup waits this long for MCP servers' first connection; slower ones join the registry later.
    private static readonly TimeSpan _mcpStartupWait = TimeSpan.FromSeconds(10);

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        ScanMainFile();

        // Resolving the hot-reloaded files loads them; an invalid file throws here and stops startup.
        services.GetRequiredService<Core.Configuration.YamlConfigFile<RiskRulesFile>>();
        services.GetRequiredService<Core.Configuration.YamlConfigFile<SatellitesFile>>();

        Directory.CreateDirectory(paths.Value.Data);
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            var initializer = new DatabaseInitializer(db, timeProvider, loggers.CreateLogger<DatabaseInitializer>());
            await initializer.InitializeAsync(new InstanceOwnerSeed(owner.Value.DisplayName, owner.Value.Language, await instanceTimeZone.GetAsync(cancellationToken)), cancellationToken);
        }

        SecretBindingReport[] reports =
        [
            await secretBinder.BindAsync(models.Value, "", cancellationToken),
            await secretBinder.BindAsync(auth.Value, "auth", cancellationToken),
            await secretBinder.BindAsync(homeAssistant.Value, "ha", cancellationToken),
            await secretBinder.BindAsync(printers.Value, "", cancellationToken),
        ];
        List<string> fromConfiguration = [.. reports.SelectMany(report => report.BoundFromConfiguration)];
        if (fromConfiguration.Count > 0)
            throw new InvalidOperationException($"Secret values must not come from configuration: {string.Join(", ", fromConfiguration)}.");
        List<string> missingRequired = [.. reports.SelectMany(report => report.MissingRequired)];
        if (missingRequired.Count > 0)
            throw new InvalidOperationException($"Required secrets do not resolve: {string.Join(", ", missingRequired)}.");

        secretHealth.Set([.. reports.SelectMany(report => report.MissingOptional)]);
        await StartMcpServersAsync(cancellationToken);
        ToolSnapshot tools = await registry.RefreshAsync(cancellationToken);
        foreach (ToolSourceError error in tools.Errors)
            LogToolSourceRejected(loggers.CreateLogger<MarshalStartup>(), error.Source, error.Message);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Invalid server files, skills and templates are skipped (the others load); a literal secret in any of them stops startup.
    private async Task StartMcpServersAsync(CancellationToken cancellationToken)
    {
        IMcpServerCatalog catalog = services.GetRequiredService<IMcpServerCatalog>();
        List<ConfigFileError> leaks =
        [
            .. catalog.Errors
                .Concat(services.GetRequiredService<ISkillCatalog>().Errors)
                .Concat(services.GetRequiredService<ITemplateCatalog>().Errors)
                .Where(error => error.LiteralSecret),
        ];
        if (leaks.Count > 0)
            throw new InvalidOperationException(string.Join(" ", leaks.Select(error => error.Message)));

        ILogger logger = loggers.CreateLogger<MarshalStartup>();
        foreach (ConfigFileError error in catalog.Errors)
            LogMcpServerRejected(logger, error.File, error.Message);

        await services.GetRequiredService<McpToolSources>().StartAsync(_mcpStartupWait, cancellationToken);
    }

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ScanMainFile()
    {
        string path = Path.Combine(paths.Value.Config, MarshalConfiguration.MainFile);
        if (!File.Exists(path))
            return;

        Result<IReadOnlyList<ConfigSecretFinding>> scan = scanner.Scan(MarshalConfiguration.MainFile, File.ReadAllText(path));
        if (scan.IsFailure)
            throw new InvalidOperationException(scan.Error.Message);
        if (scan.Value.Count > 0)
            throw new InvalidOperationException(string.Join(" ", scan.Value));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool source {Source} rejected: {Reason}")]
    private static partial void LogToolSourceRejected(ILogger logger, string source, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "MCP server file {File} rejected: {Reason}")]
    private static partial void LogMcpServerRejected(ILogger logger, string file, string reason);
}
