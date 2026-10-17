using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;
using Marshal.Data.Audit;
using Marshal.Data.Conversations;
using Marshal.Data.Identity;
using Marshal.Data.Persistence;
using Marshal.Data.Secrets;
using Marshal.Data.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Data;

/// <summary>
/// Registers persistence. The connection string is resolved when the first context is created.
/// </summary>
public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalData(this IServiceCollection services, IConfiguration configuration, Func<IServiceProvider, string> connectionString)
    {
        services.AddDbContextFactory<MarshalDbContext>((provider, options) => options.UseMarshalSqlite(connectionString(provider)));
        services.AddOptions<RecordingOptions>().Bind(configuration.GetSection("recording"));

        services.AddSingleton<IPendingActionStore, EfPendingActionStore>();
        services.AddSingleton<EfSessionStore>();
        services.AddSingleton<ISessionStore>(provider => provider.GetRequiredService<EfSessionStore>());
        services.AddSingleton<ISessionToolNamespaces>(provider => provider.GetRequiredService<EfSessionStore>());
        services.AddSingleton<IToolLatencyStats, RecordedToolLatencyStats>();
        services.AddSingleton<IConversationHistory, EfConversationHistory>();
        services.AddSingleton<IConversationReader, EfConversationReader>();
        services.AddSingleton<ISummaryWatermark, EfSummaryWatermark>();
        services.AddSingleton<IRunRecorder, DbRunRecorder>();
        services.AddSingleton<IToolRedaction, RecordingToolRedaction>();
        services.AddSingleton<IAdvisorUsage, EfAdvisorUsage>();
        services.AddSingleton<IRunQueries, EfRunQueries>();
        services.AddSingleton<IAuditLog, DbAuditLog>();
        services.AddSingleton<IUserProfileProvider, EfUserProfileProvider>();
        services.AddSingleton<IUserPreferencesStore, EfUserPreferencesStore>();
        services.AddSingleton<IInstanceOwner, EfInstanceOwner>();
        services.AddSingleton<IUserDirectory, EfUserDirectory>();
        services.AddSingleton<IApiTokenService, ApiTokenService>();
        services.AddSingleton<IExecutorCallbackTokens, ExecutorCallbackTokens>();
        services.AddSingleton<DatabaseSecretSource>();
        services.AddSingleton<ISecretSource>(provider => provider.GetRequiredService<DatabaseSecretSource>());
        services.AddSingleton<ISecretCatalog>(provider => provider.GetRequiredService<DatabaseSecretSource>());
        services.AddSingleton<ISecretWriter>(provider => provider.GetRequiredService<DatabaseSecretSource>());
        return services;
    }
}
