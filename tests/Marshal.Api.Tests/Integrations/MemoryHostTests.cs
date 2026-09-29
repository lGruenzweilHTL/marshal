using System.Net.Http.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;
using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Http;
using Marshal.Memory.Consolidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Api.Tests.Integrations;

public sealed class MemoryHostTests
{
    [Fact]
    public async Task Host_RegistersMemoryToolsAuditAndSessionSummaries()
    {
        using var factory = new MarshalApiFactory();
        _ = factory.CreateClient();
        IServiceProvider services = factory.Services;

        ToolSnapshot tools = await services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);
        Assert.NotNull(tools.FindById(new ToolId("memory", "remember")));
        Assert.NotNull(tools.FindById(new ToolId("memory", "recall")));
        Assert.NotNull(tools.FindById(new ToolId("memory", "correct_project")));
        Assert.Contains(services.GetServices<ISessionCloseListener>(), listener => listener is SessionSummaryInbox);
        Assert.Contains(services.GetServices<IHostedService>(), service => service is SessionSummaryWorker);
        Assert.NotNull(services.GetRequiredService<IMemoryAudit>());
        Assert.NotNull(services.GetRequiredService<IMemorySummarizer>());
    }

    [Fact]
    public async Task Turn_PinnedMemoryInTheSystemPrompt()
    {
        using var factory = new MarshalApiFactory();
        factory.Model.ThenText("Noted.");
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);
        OwnerId owner = await factory.Services.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
        using (factory.Services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web }))
        {
            IMemoryStore store = factory.Services.GetRequiredService<IMemoryStore>();
            ChunkId allergy = (await store.WriteAsync(new ChunkDraft(ChunkScope.General, null, ChunkType.Fact, "Allergy", "Allergic to peanuts."), ChunkAuthor.User, Ct)).Value.Id;
            await store.SetPinnedAsync(allergy, pinned: true, Ct);
        }

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/conversation/turns", new { conversationId = "c1", text = "What can I eat?", inputMode = "text" }, Ct);
        await response.Content.ReadAsStringAsync(Ct);

        Assert.Contains("Allergic to peanuts.", Assert.Single(factory.Model.Requests).SystemPrompt, StringComparison.Ordinal);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
