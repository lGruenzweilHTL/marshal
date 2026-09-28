using Marshal.Abstractions;
using Marshal.Abstractions.Tools;
using Marshal.Api.Configuration;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Marshal.Core.Models;
using Marshal.Core.Satellites;
using Marshal.Core.Secrets;
using Marshal.Data;
using Marshal.Data.Persistence;
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
    IOptions<ModelRoutingOptions> models,
    IConfigSecretScanner scanner,
    IDbContextFactory<MarshalDbContext> contexts,
    SecretBinder secretBinder,
    SecretHealth secretHealth,
    IToolRegistry registry,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILoggerFactory loggers) : IHostedLifecycleService
{
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
            await initializer.InitializeAsync(new InstanceOwnerSeed(owner.Value.DisplayName, owner.Value.Language, owner.Value.TimeZone), cancellationToken);
        }

        SecretBindingReport secrets = await secretBinder.BindAsync(models.Value, "", cancellationToken);
        if (secrets.BoundFromConfiguration.Count > 0)
            throw new InvalidOperationException($"Secret values must not come from configuration: {string.Join(", ", secrets.BoundFromConfiguration)}.");
        if (secrets.MissingRequired.Count > 0)
            throw new InvalidOperationException($"Required secrets do not resolve: {string.Join(", ", secrets.MissingRequired)}.");

        secretHealth.Set(secrets.MissingOptional);
        await registry.RefreshAsync(cancellationToken);
        foreach (ToolSourceError error in registry.Errors)
            LogToolSourceRejected(loggers.CreateLogger<MarshalStartup>(), error.Source, error.Message);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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
}
