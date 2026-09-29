using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Notes;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.Notes.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Integrations.Notes.Tests.Tools;

public sealed class NotesToolsTests
{
    private readonly FakeNotesSource _source = new();
    private readonly FakeNotesIndex _index = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tools_SafeUntrustedSensitiveWithNotesTaint()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync();

        Assert.Equal(["notes.list", "notes.read", "notes.search"], tools.Select(tool => tool.Descriptor.Id.ToString()).Order(StringComparer.Ordinal));
        Assert.All(tools, tool =>
        {
            Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier);
            Assert.True(tool.Descriptor.UntrustedOutput && tool.Descriptor.Sensitive);
            Assert.Equal("notes", tool.Descriptor.TaintKind);
            Assert.False(tool.Descriptor.Persists);
        });
    }

    [Fact]
    public async Task Hidden_WhenNotConfigured()
    {
        _source.IsConfigured = false;

        foreach (ITool tool in await ToolsAsync())
            Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task Read_ReferenceIsThePathForRedaction()
    {
        ToolOutput output = (await InvokeAsync("read", new { path = "projects/marshal.md" })).Value;

        Assert.Contains("HA Assist first.", output.Content, StringComparison.Ordinal);
        Assert.Equal("projects/marshal.md", output.Reference);
    }

    [Fact]
    public async Task Read_PathTraversal_FailsWithoutReading()
    {
        Result<ToolOutput> read = await InvokeAsync("read", new { path = "../../etc/passwd" });

        Assert.Equal("notes.path_invalid", read.Error.Code);
    }

    [Fact]
    public async Task List_Folder()
    {
        ToolOutput output = (await InvokeAsync("list", new { folder = "projects" })).Value;

        Assert.Equal("projects", _source.LastFolder);
        using var files = JsonDocument.Parse(output.Content);
        Assert.Equal("projects/marshal.md", files.RootElement[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Search_HitsWithPathsAsReferences()
    {
        ToolOutput output = (await InvokeAsync("search", new { query = "voice", limit = 3 })).Value;

        Assert.Equal(("voice", 3), _index.LastQuery);
        using var hits = JsonDocument.Parse(output.Content);
        Assert.Equal(("projects/marshal.md", "Voice"), (hits.RootElement[0].GetProperty("path").GetString(), hits.RootElement[0].GetProperty("heading").GetString()));
        Assert.Equal("projects/marshal.md#Voice", output.Reference);
    }

    [Fact]
    public void Registration_AddsNoContextContributor()
    {
        var services = new ServiceCollection();
        services.AddMarshalNotes(new ConfigurationBuilder().Build(), _ => Path.GetTempPath());

        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IContextContributor));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IToolSource));
    }

    private Task<IReadOnlyList<ITool>> ToolsAsync() => new NotesToolSource(_source, _index).GetToolsAsync(Ct);

    private async Task<Result<ToolOutput>> InvokeAsync(string name, object arguments)
    {
        ITool tool = (await ToolsAsync()).Single(tool => tool.Descriptor.Id.Name == name);
        return await tool.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), TestExecutionContext.Text()), Ct);
    }

    private sealed class FakeNotesSource : INotesSource
    {
        public bool IsConfigured { get; set; } = true;

        public string? LastFolder { get; private set; }

        public Task<Result<IReadOnlyList<NoteFile>>> ListAsync(string? folder, CancellationToken cancellationToken)
        {
            LastFolder = folder;
            return Task.FromResult<Result<IReadOnlyList<NoteFile>>>(new List<NoteFile> { new("projects/marshal.md", 42, DateTimeOffset.UnixEpoch) });
        }

        public Task<Result<string>> ReadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<Result<string>>(path == "projects/marshal.md" ? "# Marshal\nHA Assist first." : NotesErrors.PathInvalid(path));

        public Task<Result> SyncAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    private sealed class FakeNotesIndex : INotesIndex
    {
        public (string Query, int Limit)? LastQuery { get; private set; }

        public Task<Result<NotesIndexUpdate>> UpdateAsync(INotesSource source, CancellationToken cancellationToken) =>
            Task.FromResult<Result<NotesIndexUpdate>>(new NotesIndexUpdate([], []));

        public Task<IReadOnlyList<NoteHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
        {
            LastQuery = (query, limit);
            return Task.FromResult<IReadOnlyList<NoteHit>>([new NoteHit("projects/marshal.md", "Voice", "HA Assist first.", 0.8)]);
        }
    }
}
