using System.Collections;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Configuration;
using Marshal.Core.Context;
using Marshal.Core.Conversation;
using Marshal.Core.Execution;
using Marshal.Core.Guard;
using Marshal.Core.Identity;
using Marshal.Core.LiveEvents;
using Marshal.Core.Mcp;
using Marshal.Core.Models;
using Marshal.Core.Recording;
using Marshal.Core.Satellites;
using Marshal.Core.Secrets;
using Marshal.Core.Sessions;
using Marshal.Core.Skills;
using Marshal.Core.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Core;

/// <summary>
/// Registers the agent harness.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    public const string RiskFile = "risk.yaml";
    public const string SatellitesFileName = "satellites.yaml";
    public const string McpDirectory = "mcp";
    public const string SkillsDirectory = "skills";

    public static IServiceCollection AddMarshalCore(this IServiceCollection services, IConfiguration configuration, IDictionary environment)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IExecutionContextAccessor, AsyncLocalExecutionContextAccessor>();

        services.AddOptions<PathsOptions>().Bind(configuration.GetSection("paths"));
        services.AddOptions<AgentOptions>().Bind(configuration.GetSection("agent")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ConfirmationOptions>().Bind(configuration.GetSection("confirmation")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<SessionOptions>().Bind(configuration.GetSection("sessions")).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ModelRoutingOptions>().Bind(configuration).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ModelRoutingOptions>, ModelRoutingOptionsValidator>();

        // Hot-reloaded /config files; a failing first load fails startup.
        services.AddSingleton<IConfigSecretScanner, ConfigSecretScanner>();
        services.AddSingleton<IConfigWatcher>(provider => new ConfigDirectoryWatcher(provider.GetRequiredService<IOptions<PathsOptions>>().Value.Config));
        services.AddSingleton(provider => LoadConfigFile<RiskRulesFile>(provider, RiskFile, RiskRulesFile.Validate));
        services.AddSingleton<IOptionsMonitor<RiskRulesFile>>(provider => provider.GetRequiredService<YamlConfigFile<RiskRulesFile>>());
        services.AddSingleton(provider => LoadConfigFile<SatellitesFile>(provider, SatellitesFileName, SatellitesFile.Validate));
        services.AddSingleton<IOptionsMonitor<SatellitesFile>>(provider => provider.GetRequiredService<YamlConfigFile<SatellitesFile>>());

        services.AddSingleton<IModelRouter, ConfiguredModelRouter>();
        services.AddSingleton<ICostCalculator, PriceTableCostCalculator>();

        services.AddSingleton<ITool, AskAdvisorTool>();
        services.AddSingleton<IToolSource, BuiltInToolSource>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();

        services.AddHttpClient(McpTransportFactory.HttpClientName);
        services.AddSingleton(provider => new YamlMcpServerCatalog(
            Path.Combine(provider.GetRequiredService<IOptions<PathsOptions>>().Value.Config, McpDirectory),
            provider.GetRequiredService<IConfigWatcher>(),
            $"{McpDirectory}/*.yaml",
            provider.GetRequiredService<IConfigSecretScanner>(),
            provider.GetRequiredService<ISecretStore>(),
            provider.GetRequiredService<ILogger<YamlMcpServerCatalog>>()));
        services.AddSingleton<IMcpServerCatalog>(provider => provider.GetRequiredService<YamlMcpServerCatalog>());
        services.AddSingleton<IMcpTransportFactory, McpTransportFactory>();
        services.AddSingleton(McpReconnectPolicy.Default);
        services.AddSingleton<McpToolSources>();
        services.AddSingleton<IToolSourceProvider>(provider => provider.GetRequiredService<McpToolSources>());
        services.AddSingleton<ISecretReferenceSource, McpSecretReferences>();

        services.AddSingleton(provider => new FileSkillCatalog(
            Path.Combine(provider.GetRequiredService<IOptions<PathsOptions>>().Value.Config, SkillsDirectory),
            provider.GetRequiredService<IConfigWatcher>(),
            $"{SkillsDirectory}/**/*",
            provider.GetRequiredService<IConfigSecretScanner>(),
            provider.GetRequiredService<ILogger<FileSkillCatalog>>()));
        services.AddSingleton<ISkillCatalog>(provider => provider.GetRequiredService<FileSkillCatalog>());
        services.AddSingleton<IContextContributor, SkillIndexContributor>();
        services.AddSingleton<ITool, LoadSkillTool>();

        services.AddSingleton<IRiskEngine, RuleBasedRiskEngine>();
        services.AddSingleton<IToolVisibilityFilter, ToolVisibilityFilter>();
        services.AddSingleton<IGuard, ToolCallGuard>();
        services.AddSingleton<IConfirmationKeywords, ConfiguredConfirmationKeywords>();
        services.AddSingleton<IConfirmationJudge, LlmConfirmationJudge>();
        services.AddSingleton<IConfirmationFlow, ConfirmationFlow>();

        services.AddSingleton<IAreaDirectory, IdAreaDirectory>();
        services.AddSingleton<ISatelliteContextProvider, YamlSatelliteContextProvider>();
        services.AddSingleton<IContextContributor, BasePromptContributor>();
        services.AddSingleton<IContextContributor, UserProfileContributor>();
        services.AddSingleton<IContextContributor, TimeLocaleContributor>();
        services.AddSingleton<IContextContributor, SatelliteContextContributor>();
        services.AddSingleton<IContextContributor, OutputModeContributor>();
        services.AddSingleton<IContextBuilder, ContextBuilder>();

        services.AddSingleton<IAgentRunner, AgentRunner>();
        services.AddSingleton<IIdentityResolver, InstanceOwnerIdentityResolver>();
        services.AddOptions<InstanceOptions>().Bind(configuration.GetSection("instance"))
            .Validate(options => options.TimeZone is null || UserPreferencesRules.IsIanaTimeZone(options.TimeZone), "instance.timeZone is not an IANA time zone.")
            .ValidateOnStart();
        services.AddSingleton<IInstanceTimeZone, ConfiguredInstanceTimeZone>();
        services.AddSingleton<SessionResolver>();
        services.AddSingleton<SessionMaintenance>();
        services.AddSingleton<ConversationService>();
        services.AddHostedService<IdleSessionCloser>();

        services.AddSingleton(provider => new EnvironmentFileSecretSource(environment, provider.GetRequiredService<IOptions<PathsOptions>>().Value.Secrets));
        services.AddSingleton<ISecretSource>(provider => provider.GetRequiredService<EnvironmentFileSecretSource>());
        services.AddSingleton<ISecretCatalog>(provider => provider.GetRequiredService<EnvironmentFileSecretSource>());
        services.AddSingleton<ISecretReferenceSource>(new ConfigurationSecretReferences(configuration));
        services.AddSingleton<ISecretStore, SecretStore>();
        services.AddSingleton<SecretBinder>();
        services.AddSingleton<SecretHealth>();

        services.AddSingleton<OpenTelemetryRunObserver>();
        services.AddSingleton<IRunObserver>(provider => provider.GetRequiredService<OpenTelemetryRunObserver>());

        services.AddSingleton<InProcessLiveEvents>();
        services.AddSingleton<ILiveEventPublisher>(provider => provider.GetRequiredService<InProcessLiveEvents>());
        services.AddSingleton<ILiveEventSource>(provider => provider.GetRequiredService<InProcessLiveEvents>());
        services.AddSingleton(new LiveTopic(LiveTopics.Runs));
        services.AddSingleton<IRunObserver, LiveRunObserver>();
        return services;
    }

    private static YamlConfigFile<T> LoadConfigFile<T>(IServiceProvider provider, string name, Func<T, Abstractions.Result> validate)
        where T : class, new()
    {
        string directory = provider.GetRequiredService<IOptions<PathsOptions>>().Value.Config;
        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Marshal.Configuration");
        return YamlConfigFile.Load(directory, name, provider.GetRequiredService<IConfigWatcher>(), provider.GetRequiredService<IConfigSecretScanner>(), validate, logger);
    }
}
