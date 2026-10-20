using Marshal.Abstractions;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Notes;
using Marshal.Integrations.Notes.Tools;

namespace Marshal.Integrations.Notes.Tests.Tools;

public sealed class NoteContentResolverTests
{
    private readonly Notes _source = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Path_NoteTextWithNotesTaint()
    {
        Result<UntrustedItem> item = await new NoteContentResolver(_source).ResolveAsync("projects/voron.md", Ct);

        Assert.Equal(("notes", "note:projects/voron.md", "# Voron\nNozzle 0.4"), (item.Value.TaintKind, item.Value.Reference, item.Value.Text));
    }

    [Theory]
    [InlineData("missing.md")]
    [InlineData("../etc/passwd")]
    public async Task UnknownPath_NotFound(string path)
    {
        Result<UntrustedItem> item = await new NoteContentResolver(_source).ResolveAsync(path, Ct);

        Assert.Equal(ExtractionAnswer.NotFoundCode, item.Error.Code);
    }

    [Fact]
    public async Task NotConfigured_UnavailableAndNotFound()
    {
        _source.IsConfigured = false;
        var resolver = new NoteContentResolver(_source);

        Assert.False(await resolver.IsAvailableAsync(Ct));
        Assert.Equal(ExtractionAnswer.NotFoundCode, (await resolver.ResolveAsync("projects/voron.md", Ct)).Error.Code);
    }

    private sealed class Notes : INotesSource
    {
        public bool IsConfigured { get; set; } = true;

        public Task<Result<IReadOnlyList<NoteFile>>> ListAsync(string? folder, CancellationToken cancellationToken) =>
            Task.FromResult<Result<IReadOnlyList<NoteFile>>>(new List<NoteFile>());

        public Task<Result<string>> ReadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(path == "projects/voron.md"
                ? (Result<string>)"# Voron\nNozzle 0.4"
                : new Error("notes.not_found", "No such note.", ErrorType.NotFound));

        public Task<Result> SyncAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
