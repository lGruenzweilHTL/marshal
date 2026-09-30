using Marshal.Abstractions.Context;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Memory.Browsing;
using Marshal.Memory.Consolidation;
using Marshal.Memory.Projects;
using Marshal.Memory.Retrieval;
using Marshal.Memory.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Memory;

/// <summary>
/// Registers memory: store, retrieval, project activation, the memory context contributors and tools, session-close
/// summarization and the audit. Needs the core services (embedding generator, execution context, area directory).
/// </summary>
public static class MemoryServiceCollectionExtensions
{
    public const string Section = "memory";

    public static IServiceCollection AddMarshalMemory(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MemoryOptions>().Bind(configuration.GetSection(Section));

        services.AddSingleton<IMemoryStore, SqliteMemoryStore>();
        services.AddSingleton<IMemoryExporter, NdjsonMemoryExporter>();
        services.AddSingleton<IChunkScorer, ChunkScorer>();
        services.AddSingleton<IMemoryRetriever, HybridMemoryRetriever>();
        services.AddSingleton<IMemoryAudit, MemoryAudit>();
        services.AddSingleton<IMemoryBrowser, MemoryBrowser>();
        services.AddSingleton<IMemoryMaps, MemoryMaps>();
        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<MemoryIndexRebuilder>();
        services.AddSingleton<IHostedService, MemoryIndexStartup>();

        services.AddSingleton<IMemoryProjects, MemoryProjects>();
        services.AddSingleton<ProjectSessions>();
        services.AddSingleton<ProjectLearning>();
        services.AddSingleton<IProjectActivator, ProjectActivator>();
        services.AddSingleton<ITurnMemory, TurnMemory>();

        services.AddSingleton<IContextContributor, PinnedMemoryContributor>();
        services.AddSingleton<IContextContributor, RetrievedMemoryContributor>();
        services.AddSingleton<ITool, MemoryRememberTool>();
        services.AddSingleton<ITool, MemoryRecallTool>();
        services.AddSingleton<ITool, MemoryCorrectProjectTool>();

        services.AddSingleton<IMemorySummarizer, LlmMemorySummarizer>();
        services.AddSingleton<SessionSummaryInbox>();
        services.AddSingleton<ISessionCloseListener>(provider => provider.GetRequiredService<SessionSummaryInbox>());
        services.AddSingleton<IHostedService, SessionSummaryWorker>();
        services.AddSingleton<ISystemJob, ConsolidationJob>();
        return services;
    }
}
