using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Tools;
using Marshal.Memory.Projects;
using Marshal.Memory.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests.Tools;

public sealed class MemoryRememberToolTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private TestDatabase _database = null!;
    private SqliteMemoryStore _store = null!;
    private MemoryRememberTool _tool = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _store = MemoryTestKit.Store(_database, _embeddings, _time);
        _tool = new MemoryRememberTool(_store, new MemoryProjects(_database.Factory));
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    // The guard escalates safe tools flagged Persists in tainted runs to a pending action with a taint reason naming
    // the source (GuardTests.Evaluate_PersistsInTaintedRun_EscalatedWithTaintReasonNamingMail); the flags are the contract.
    [Fact]
    public void Descriptor_SafePersistsAndWithoutPinArgument()
    {
        Assert.Equal((new ToolId("memory", "remember"), RiskTier.Safe, true), (_tool.Descriptor.Id, _tool.Descriptor.DefaultTier, _tool.Descriptor.Persists));
        Assert.False(_tool.Descriptor.ParametersSchema.GetProperty("properties").TryGetProperty("pinned", out _));
    }

    [Fact]
    public async Task Remember_CleanRun_WritesDeviceChunkByTheAgent()
    {
        Result<ToolOutput> result = await RememberAsync(new { type = "device", title = "Voron nozzle", content = "The Voron uses a 0.6 mm nozzle.", importance = 4 });

        Assert.True(result.IsSuccess);
        MemoryChunk chunk = await SingleAsync("Voron nozzle", ChunkType.Device);
        Assert.Equal((ChunkScope.General, "The Voron uses a 0.6 mm nozzle.", 4, ChunkAuthor.Agent, false), (chunk.Scope, chunk.Content, chunk.Importance, chunk.CreatedBy, chunk.Pinned));
    }

    [Fact]
    public async Task Remember_PinnedArgument_IgnoredChunkNotPinned()
    {
        Assert.True((await RememberAsync(new { type = "fact", title = "Allergy", content = "Allergic to peanuts.", pinned = true })).IsSuccess);

        Assert.False((await SingleAsync("Allergy", ChunkType.Fact)).Pinned);
    }

    [Fact]
    public async Task Remember_SameIdentity_UpdatesInPlaceKeepingVersion()
    {
        await RememberAsync(new { type = "device", title = "Voron nozzle", content = "0.4 mm nozzle." });

        Result<ToolOutput> second = await RememberAsync(new { type = "device", title = "voron  NOZZLE", content = "0.6 mm nozzle." });

        Assert.Contains("updated", second.Value.Content, StringComparison.Ordinal);
        MemoryChunk chunk = await SingleAsync("Voron nozzle", ChunkType.Device);
        Assert.Equal("0.6 mm nozzle.", chunk.Content);
        Assert.Equal("0.4 mm nozzle.", Assert.Single(await _store.GetVersionsAsync(chunk.Id, Ct)).Content);
    }

    [Fact]
    public async Task Remember_Project_WritesProjectScopedChunk()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron");

        Assert.True((await RememberAsync(new { type = "procedure", title = "Calibration", content = "Run the z offset macro first.", project = "voron", sourceRef = "notes/voron.md" })).IsSuccess);

        MemoryChunk chunk = (await _store.GetAsync(ChunkIds.Compute(TestExecutionContext.OwnerA, ChunkScope.Project, voron, ChunkType.Procedure, "Calibration"), Ct))!;
        Assert.Equal((ChunkScope.Project, voron, "notes/voron.md"), (chunk.Scope, chunk.ProjectId, chunk.SourceRef));
    }

    [Fact]
    public async Task Remember_InvalidArguments_Errors()
    {
        Result<ToolOutput> missingContent = await RememberAsync(new { type = "fact", title = "Allergy" });
        Result<ToolOutput> badType = await RememberAsync(new { type = "rumour", title = "A", content = "B" });
        Result<ToolOutput> badImportance = await RememberAsync(new { type = "fact", title = "A", content = "B", importance = 9 });
        Result<ToolOutput> unknownProject = await RememberAsync(new { type = "fact", title = "A", content = "B", project = "Nope" });
        Result<ToolOutput> projectScopeWithoutProject = await RememberAsync(new { type = "fact", title = "A", content = "B", scope = "project" });

        Assert.Equal("memory.invalid_arguments", missingContent.Error.Code);
        Assert.Equal("memory.invalid_arguments", badType.Error.Code);
        Assert.Equal("memory.invalid_arguments", badImportance.Error.Code);
        Assert.Equal("memory.project_not_found", unknownProject.Error.Code);
        Assert.Equal("memory.invalid_arguments", projectScopeWithoutProject.Error.Code);
    }

    private Task<Result<ToolOutput>> RememberAsync(object arguments) =>
        _tool.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), TestExecutionContext.Text()), Ct);

    private async Task<MemoryChunk> SingleAsync(string title, ChunkType type) =>
        (await _store.GetAsync(ChunkIds.Compute(TestExecutionContext.OwnerA, ChunkScope.General, null, type, title), Ct))!;
}
