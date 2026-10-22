using System.Collections;
using Marshal.Abstractions.Apps;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Displays;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Health;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.AiTasks;
using Marshal.Core.McpServer;
using Marshal.Core.Calendar;
using Marshal.Core.Configuration;
using Marshal.Core.Evals;
using Marshal.Core.Extraction;
using Marshal.Core.Context;
using Marshal.Core.Displays;
using Marshal.Core.Music;
using Marshal.Core.Conversation;
using Marshal.Core.Execution;
using Marshal.Core.Guard;
using Marshal.Core.Health;
using Marshal.Core.Identity;
using Marshal.Core.LiveEvents;
using Marshal.Core.Mcp;
using Marshal.Core.Models;
using Marshal.Core.Recording;
using Marshal.Core.Satellites;
using Marshal.Core.Secrets;
using Marshal.Core.Sessions;
using Marshal.Core.Skills;
using Marshal.Core.Templates;
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
    public const string TemplatesDirectory = "templates";

    public static IServiceCollection AddMarshalCore(this IServiceCollection services, IConfiguration configuration, IDictionary environment)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IExecutionContextAccessor, AsyncLocalExecutionContextAccessor>();

        services.AddOptions<PathsOptions>().Bind(configuration.GetSection("paths"));
        services.AddOptions<AgentOptions>().Bind(configuration.GetSection("agent")).ValidateAttributes().ValidateOnStart();
        services.AddOptions<ConfirmationOptions>().Bind(configuration.GetSection("confirmation")).ValidateAttributes().ValidateOnStart();
        services.AddOptions<ToolsOptions>().Bind(configuration.GetSection("tools")).ValidateAttributes().ValidateOnStart();
        services.AddOptions<VoiceOptions>().Bind(configuration.GetSection("voice")).ValidateAttributes().ValidateOnStart();
        services.AddOptions<AiTaskOptions>().Bind(configuration.GetSection(AiTaskOptions.Section)).ValidateAttributes().ValidateOnStart();
        services.AddOptions<SessionOptions>().Bind(configuration.GetSection("sessions")).ValidateAttributes().ValidateOnStart();
        services.AddOptions<ModelRoutingOptions>().Bind(configuration).ValidateOnStart();
        services.AddSingleton<IValidateOptions<ModelRoutingOptions>, ModelRoutingOptionsValidator>();

        // Hot-reloaded /config files; a failing first load fails startup.
        services.AddSingleton<IConfigSecretScanner, ConfigSecretScanner>();
        services.AddSingleton<IConfigWatcher>(provider => new ConfigDirectoryWatcher(provider.GetRequiredService<IOptions<PathsOptions>>().Value.Config));
        services.AddOptions<DisplayOptions>().Bind(configuration.GetSection("displays")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DisplayOptions>, DisplayOptionsValidator>();
        services.AddSingleton<IDisplayDirectory, ConfiguredDisplayDirectory>();
        services.AddOptions<MusicOptions>().Bind(configuration.GetSection("music")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MusicOptions>, MusicOptionsValidator>();
        services.AddSingleton<ISpeakerDirectory, ConfiguredSpeakerDirectory>();
        AddConfigFile<RiskRulesFile>(services, RiskFile, RiskRulesFile.Validate);
        // Display and speaker names straight from configuration: reading invalid options would throw before their errors are reported.
        AddConfigFile<SatellitesFile>(services, SatellitesFileName, file => SatellitesFile.Validate(
            file,
            [.. configuration.GetSection("displays:surfaces").GetChildren().Select(display => display.Key)],
            [.. configuration.GetSection("music:speakers").GetChildren().Select(speaker => speaker.Key)]));

        services.AddSingleton<IModelRouter, ConfiguredModelRouter>();
        services.AddSingleton<ICostCalculator, PriceTableCostCalculator>();
        // ModelRoutingOptionsValidator rejects a missing embedding section at startup, before anything resolves this.
        services.AddSingleton(provider => provider.GetRequiredService<IEmbeddingGeneratorFactory>().Create(
            provider.GetRequiredService<IOptions<ModelRoutingOptions>>().Value.Embedding!));

        services.AddSingleton(new ToolNamespace("agent", "Marshal itself: skills, delegating to subagents, the advisor, finding more tools, moving work to the background"));
        services.AddSingleton<ITool, AskAdvisorTool>();
        // Quarantined extraction: typed questions about untrusted items, answered by a tool-less fast model.
        services.AddSingleton<IQuarantinedExtractor, ModelQuarantinedExtractor>();
        services.AddSingleton<ITool, ExtractTool>();
        services.AddSingleton<ITool>(provider => new SystemHealthTool(provider.GetService<ISystemHealth>()));
        services.AddSingleton(new ToolNamespace("system", "Marshal's own health: problems with integrations, configuration and approvals that need an admin"));
        services.AddSingleton<IToolSource, BuiltInToolSource>();
        // The calendar sources (HA, CalDAV) register themselves; the tools see them through the composite.
        services.AddSingleton<ICalendarProvider, CompositeCalendarProvider>();
        services.AddSingleton<IToolSource, CalendarToolSource>();
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<INamespaceEmbeddings, CachedNamespaceEmbeddings>();
        services.AddSingleton<IToolSelector, NamespaceToolSelector>();
        services.AddSingleton<ITool>(provider => new FindToolsTool(
            () => provider.GetRequiredService<IToolVisibilityFilter>(),
            provider.GetRequiredService<IToolSelector>(),
            provider.GetRequiredService<IOptions<ToolsOptions>>()));

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
        services.AddSingleton(provider => new McpToolPinner(
            provider.GetRequiredService<IMcpToolPins>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IInstanceOwner>(),
            provider.GetService<INotificationSender>(),
            provider.GetService<IUserProfileProvider>(),
            provider.GetRequiredService<ILogger<McpToolPinner>>()));
        services.AddSingleton(provider => new McpToolSources(
            provider.GetRequiredService<IMcpServerCatalog>(),
            provider.GetRequiredService<IMcpTransportFactory>(),
            provider.GetRequiredService<ILoggerFactory>(),
            provider.GetRequiredService<McpReconnectPolicy>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<McpToolPinner>(),
            // Resolved per request: the queue needs the tool registry, which needs these sources.
            () => provider.GetService<IInputRequestQueue>(),
            () => provider.GetService<IMcpTaskTracker>()));
        services.AddSingleton<IMcpTaskClient>(provider => provider.GetRequiredService<McpToolSources>());
        services.AddSingleton<IMcpAppResources>(provider => provider.GetRequiredService<McpToolSources>());
        services.AddSingleton<IHealthWarningProvider, McpPendingToolWarnings>();
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

        services.AddSingleton(provider => new FileTemplateCatalog(
            Path.Combine(provider.GetRequiredService<IOptions<PathsOptions>>().Value.Config, TemplatesDirectory),
            provider.GetRequiredService<IConfigWatcher>(),
            $"{TemplatesDirectory}/*.md",
            provider.GetRequiredService<IConfigSecretScanner>(),
            provider.GetRequiredService<ILogger<FileTemplateCatalog>>()));
        services.AddSingleton<ITemplateCatalog>(provider => provider.GetRequiredService<FileTemplateCatalog>());
        services.AddSingleton<IContextContributor, TemplatePromptContributor>();
        services.AddSingleton<IContextContributor, DelegationIndexContributor>();
        services.AddSingleton<ITool>(provider => new DelegateTool(
            () => provider.GetRequiredService<IAgentRunner>(),
            provider.GetRequiredService<ITemplateCatalog>(),
            provider.GetRequiredService<IOptions<AgentOptions>>()));

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

        services.TryAddSingleton<IToolRedaction, SensitiveToolRedaction>();
        services.AddSingleton<IToolCallSummarizer, DefaultToolCallSummarizer>();
        services.AddSingleton<IToolCallPipeline, GuardedToolCallPipeline>();
        services.AddSingleton<IAgentRunner, AgentRunner>();
        services.AddSingleton<IStructuredOutputShaper, ModelStructuredOutputShaper>();
        services.AddSingleton<IAiTaskRunner, AiTaskRunner>();
        services.AddSingleton<IAskMarshalRunner, AskMarshalRunner>();
        services.AddSingleton<IIdentityResolver, InstanceOwnerIdentityResolver>();
        services.AddOptions<InstanceOptions>().Bind(configuration.GetSection("instance"))
            .Validate(options => options.TimeZone is null || UserPreferencesRules.IsIanaTimeZone(options.TimeZone), "instance.timeZone is not an IANA time zone.")
            .ValidateOnStart();
        services.AddSingleton<IInstanceTimeZone, ConfiguredInstanceTimeZone>();
        services.AddSingleton<SessionResolver>();
        services.AddSingleton<SessionMaintenance>();
        services.AddSingleton<ConversationService>();
        services.AddHostedService<IdleSessionCloser>();
        services.AddHostedService<ModelReasoningCheck>();

        // Evals: replays answer tools from recordings; the runner works through queued eval runs in the background.
        services.AddSingleton<EvalModels>();
        services.AddSingleton<EvalCostEstimator>();
        services.AddSingleton<IEvalReplayer, AgentLoopReplayer>();
        services.AddSingleton<IReplyJudge>(provider => new EvaluatorReplyJudge(provider.GetRequiredService<EvalModels>(), provider.GetRequiredService<ICostCalculator>()));
        services.AddSingleton<EvalRunner>();
        services.AddSingleton<IEvalRunControl>(provider => provider.GetRequiredService<EvalRunner>());
        services.AddHostedService(provider => provider.GetRequiredService<EvalRunner>());

        services.AddSingleton(provider => new EnvironmentFileSecretSource(environment, provider.GetRequiredService<IOptions<PathsOptions>>().Value.Secrets));
        services.AddSingleton<ISecretSource>(provider => provider.GetRequiredService<EnvironmentFileSecretSource>());
        services.AddSingleton<ISecretCatalog>(provider => provider.GetRequiredService<EnvironmentFileSecretSource>());
        services.AddSingleton<ISecretReferenceSource>(new ConfigurationSecretReferences(configuration));
        services.AddSingleton<ISecretStore, SecretStore>();
        services.AddSingleton<SecretBinder>();
        services.AddSingleton<SecretHealth>();

        services.AddOptions<TelemetryOptions>().Bind(configuration.GetSection("telemetry")).ValidateOnStart();
        services.AddSingleton<OpenTelemetryRunObserver>();
        services.AddSingleton<IRunObserver>(provider => provider.GetRequiredService<OpenTelemetryRunObserver>());

        services.AddSingleton<InProcessLiveEvents>();
        services.AddSingleton<ILiveEventPublisher>(provider => provider.GetRequiredService<InProcessLiveEvents>());
        services.AddSingleton<ILiveEventSource>(provider => provider.GetRequiredService<InProcessLiveEvents>());
        services.AddSingleton(new LiveTopic(LiveTopics.Runs));
        services.AddSingleton<IRunObserver, LiveRunObserver>();
        return services;
    }

    // The load result is a service of its own so the startup configuration stage can report a broken file with the
    // other errors; the file itself is only resolved once that stage passed.
    private static void AddConfigFile<T>(IServiceCollection services, string name, Func<T, Abstractions.Result> validate)
        where T : class, new()
    {
        services.AddSingleton(provider =>
        {
            string directory = provider.GetRequiredService<IOptions<PathsOptions>>().Value.Config;
            ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Marshal.Configuration");
            return YamlConfigFile.TryLoad(directory, name, provider.GetRequiredService<IConfigWatcher>(), provider.GetRequiredService<IConfigSecretScanner>(), validate, logger);
        });
        services.AddSingleton(provider => provider.GetRequiredService<Abstractions.Result<YamlConfigFile<T>>>().Value);
        services.AddSingleton<IOptionsMonitor<T>>(provider => provider.GetRequiredService<YamlConfigFile<T>>());
    }
}
