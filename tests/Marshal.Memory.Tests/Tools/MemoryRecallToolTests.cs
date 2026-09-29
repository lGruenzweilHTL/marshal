using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Tools;
using Marshal.Memory.Projects;
using Marshal.Memory.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests.Tools;

public sealed class MemoryRecallToolTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private TestDatabase _database = null!;
    private SqliteMemoryStore _store = null!;
    private MemoryRecallTool _tool = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _store = MemoryTestKit.Store(_database, _embeddings, _time);
        _tool = new MemoryRecallTool(MemoryTestKit.Retriever(_database, _embeddings, _time), new MemoryProjects(_database.Factory), Microsoft.Extensions.Options.Options.Create(new MemoryOptions()));
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public void Descriptor_SafeWithoutPersistOrTaint()
    {
        Assert.Equal((new ToolId("memory", "recall"), RiskTier.Safe, false, false), (_tool.Descriptor.Id, _tool.Descriptor.DefaultTier, _tool.Descriptor.Persists, _tool.Descriptor.UntrustedOutput));
    }

    [Fact]
    public async Task Recall_TypeFilter_OnlyThatType()
    {
        await WriteAsync(ChunkType.Procedure, "Printer calibration", "Run the z offset macro first.");
        await WriteAsync(ChunkType.Fact, "Printer location", "The printer is in the basement.");

        string result = await RecallAsync(new { query = "printer", type = "procedure" });

        Assert.Contains("[procedure] Printer calibration", result, StringComparison.Ordinal);
        Assert.DoesNotContain("basement", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recall_ProjectByName_OnlyThatProject()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron");
        await WriteAsync(ChunkType.Procedure, "Printer calibration", "Z offset first.", voron);
        await WriteAsync(ChunkType.Procedure, "Printer cleaning", "Wipe the bed.");

        string result = await RecallAsync(new { query = "printer", project = "voron" });

        Assert.Contains("Z offset first.", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Wipe the bed.", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recall_LargerBudgetThanAutomaticRetrieval_AndLimit()
    {
        for (int index = 0; index < 12; index++)
            await WriteAsync(ChunkType.Event, $"Printer job {index}", $"Printed part {index}. {new string('x', 200)}");
        var options = new MemoryOptions();
        options.Retrieval.Quotas[ChunkType.Event] = 20;
        var tool = new MemoryRecallTool(MemoryTestKit.Retriever(_database, _embeddings, _time, options), new MemoryProjects(_database.Factory), Microsoft.Extensions.Options.Options.Create(options));

        string all = (await tool.InvokeAsync(new ToolInvocation(Json(new { query = "printer", limit = 30 }), TestExecutionContext.Text()), Ct)).Value.Content;
        string limited = (await tool.InvokeAsync(new ToolInvocation(Json(new { query = "printer", limit = 3 }), TestExecutionContext.Text()), Ct)).Value.Content;

        Assert.True(Lines(all) > 600 / 60, $"recall returned {Lines(all)} chunks");
        Assert.Equal(3, Lines(limited));
    }

    [Fact]
    public async Task Recall_UnknownProjectOrType_Errors()
    {
        Result<ToolOutput> project = await _tool.InvokeAsync(new ToolInvocation(Json(new { query = "x", project = "nope" }), TestExecutionContext.Text()), Ct);
        Result<ToolOutput> type = await _tool.InvokeAsync(new ToolInvocation(Json(new { query = "x", type = "gossip" }), TestExecutionContext.Text()), Ct);

        Assert.Equal("memory.project_not_found", project.Error.Code);
        Assert.Equal("memory.invalid_arguments", type.Error.Code);
    }

    [Fact]
    public async Task Recall_NothingFound_SaysSo()
    {
        Assert.Equal("No memory found.", await RecallAsync(new { query = "printer" }));
    }

    private async Task<string> RecallAsync(object arguments)
    {
        Result<ToolOutput> result = await _tool.InvokeAsync(new ToolInvocation(Json(arguments), TestExecutionContext.Text()), Ct);
        return result.Value.Content;
    }

    private async Task WriteAsync(ChunkType type, string title, string content, ProjectId? project = null) =>
        await _store.WriteAsync(new ChunkDraft(project is null ? ChunkScope.General : ChunkScope.Project, project, type, title, content), ChunkAuthor.User, Ct);

    private static int Lines(string text) => text.Split('\n').Count(line => line.StartsWith("- [", StringComparison.Ordinal));

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
}
