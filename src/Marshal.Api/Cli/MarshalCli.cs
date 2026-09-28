using System.Collections;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Configuration;
using Marshal.Api.Hosting;
using Marshal.Core.Configuration;
using Marshal.Core.Execution;
using Marshal.Core.Identity;
using Marshal.Data;
using Marshal.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Cli;

/// <summary>
/// Command mode of the container entry point (<c>marshal tokens ...</c>); runs before and instead of the web host.
/// </summary>
internal static class MarshalCli
{
    public static bool IsCommand(string[] args) => args is ["tokens", ..];

    public static async Task<int> RunAsync(string[] args, IDictionary environment)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddMarshalConfiguration(environment[MarshalApp.ConfigDirectoryVariable] as string ?? "/config", environment);
        builder.Services.AddLogging();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IExecutionContextAccessor, AsyncLocalExecutionContextAccessor>();
        builder.Services.AddOptions<PathsOptions>().Bind(builder.Configuration.GetSection("paths"));
        builder.Services.AddOptions<OwnerOptions>().Bind(builder.Configuration.GetSection("owner"));
        builder.Services.AddOptions<InstanceOptions>().Bind(builder.Configuration.GetSection("instance"));
        builder.Services.AddSingleton<IInstanceTimeZone, ConfiguredInstanceTimeZone>();
        builder.Services.AddMarshalData(builder.Configuration, MarshalApp.ConnectionString);

        using IHost host = builder.Build();
        IServiceProvider services = host.Services;
        OwnerOptions owner = services.GetRequiredService<IOptions<OwnerOptions>>().Value;
        Directory.CreateDirectory(services.GetRequiredService<IOptions<PathsOptions>>().Value.Data);
        await using (MarshalDbContext db = await services.GetRequiredService<IDbContextFactory<MarshalDbContext>>().CreateDbContextAsync())
        {
            await new DatabaseInitializer(db, TimeProvider.System, services.GetRequiredService<ILogger<DatabaseInitializer>>())
                .InitializeAsync(new InstanceOwnerSeed(owner.DisplayName, owner.Language, await services.GetRequiredService<IInstanceTimeZone>().GetAsync(CancellationToken.None)), CancellationToken.None);
        }

        var command = new TokenCommand(
            services.GetRequiredService<IApiTokenService>(),
            services.GetRequiredService<IAuditLog>(),
            services.GetRequiredService<IInstanceOwner>(),
            services.GetRequiredService<IExecutionContextAccessor>(),
            TimeProvider.System,
            Console.Out,
            Console.Error);
        return await command.RunAsync(args[1..], CancellationToken.None);
    }
}
