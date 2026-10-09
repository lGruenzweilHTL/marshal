using System.Security.Cryptography;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.LiveEvents;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Artifacts.Tests;

public sealed class FileArtifactStoreTests : IAsyncLifetime
{
    private const int Megabyte = 1024 * 1024;

    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-artifacts-").FullName;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero));
    private readonly ArtifactOptions _options = new();
    private readonly FakeLiveEventPublisher _live = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Create_StoresFileAndRowWithHashSizeAndKind()
    {
        byte[] content = Encoding.UTF8.GetBytes("# Notes\n");

        ArtifactRecord created = (await CreateAsync("notes.md", content, ArtifactSource.Upload)).Value;

        Assert.Equal(("notes.md", ArtifactKind.Markdown, (long)content.Length, Convert.ToHexStringLower(SHA256.HashData(content)), ArtifactSource.Upload, false, _time.GetUtcNow()),
            (created.Name, created.Kind, created.Size, created.Sha256, created.Source, created.Tainted, created.CreatedAt));
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(_directory, OwnerA.ToString(), created.Id.ToString()), Ct));
        Assert.Equal(created, (await Store().GetAsync(created.Id, Ct)).Value);
        (ArtifactRecord artifact, Stream stream) = (await Store().OpenReadAsync(created.Id, Ct)).Value;
        await using (stream)
            Assert.Equal("# Notes\n", await new StreamReader(stream).ReadToEndAsync(Ct));
        Assert.Equal(created.Id, artifact.Id);
        LiveEvent published = Assert.Single(_live.Events);
        Assert.Equal(("artifacts", "artifact.created", OwnerA, (object)created), (published.Topic, published.Type, published.Owner, published.Payload));
    }

    [Fact]
    public async Task Create_NameIsReducedToTheFileName()
    {
        ArtifactRecord created = (await CreateAsync("../../etc/passwd.txt", "x"u8.ToArray())).Value;

        Assert.Equal("passwd.txt", created.Name);
    }

    [Fact]
    public async Task OwnerScoping_GetListAndDelete()
    {
        ArtifactRecord mine = (await CreateAsync("a.md", "a"u8.ToArray())).Value;
        _database.Accessor.Current = Text(OwnerB);

        Assert.Equal(ErrorType.NotFound, (await Store().GetAsync(mine.Id, Ct)).Error.Type);
        Assert.Equal(ErrorType.NotFound, (await Store().OpenReadAsync(mine.Id, Ct)).Error.Type);
        Assert.Empty(await Store().ListAsync(new ArtifactQuery(), Ct));
        Assert.Equal(ErrorType.NotFound, (await Store().DeleteAsync(mine.Id, Ct)).Error.Type);
        Assert.Equal(0, (await Store().UsageAsync(Ct)).Used);

        _database.Accessor.Current = Text(OwnerA);
        Assert.True((await Store().GetAsync(mine.Id, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Delete_RemovesFileAndRow()
    {
        ArtifactRecord created = (await CreateAsync("a.md", "a"u8.ToArray())).Value;

        Assert.True((await Store().DeleteAsync(created.Id, Ct)).IsSuccess);

        Assert.Equal(ErrorType.NotFound, (await Store().GetAsync(created.Id, Ct)).Error.Type);
        Assert.False(File.Exists(Path.Combine(_directory, OwnerA.ToString(), created.Id.ToString())));
        Assert.Equal("artifact.deleted", _live.Events.Last().Type);
    }

    [Fact]
    public async Task List_NewestFirstFilteredByKindSourceAndName()
    {
        await CreateAsync("first.md", "a"u8.ToArray());
        _time.Advance(TimeSpan.FromMinutes(1));
        await CreateAsync("bracket.scad", "cube(1);"u8.ToArray(), ArtifactSource.Executor, tainted: true);
        _time.Advance(TimeSpan.FromMinutes(1));
        await CreateAsync("second.md", "b"u8.ToArray());

        Assert.Equal(["second.md", "bracket.scad", "first.md"], (await Store().ListAsync(new ArtifactQuery(), Ct)).Select(artifact => artifact.Name));
        Assert.Equal(["second.md", "first.md"], (await Store().ListAsync(new ArtifactQuery(Kind: ArtifactKind.Markdown), Ct)).Select(artifact => artifact.Name));
        Assert.Equal(["bracket.scad"], (await Store().ListAsync(new ArtifactQuery(Source: ArtifactSource.Executor), Ct)).Select(artifact => artifact.Name));
        Assert.Equal(["bracket.scad"], (await Store().ListAsync(new ArtifactQuery(NameContains: "BRACK"), Ct)).Select(artifact => artifact.Name));
        Assert.Empty(await Store().ListAsync(new ArtifactQuery(NameContains: "%"), Ct));
        Assert.Single(await Store().ListAsync(new ArtifactQuery(Limit: 1), Ct));
    }

    [Fact]
    public async Task QuotaExceeded_StoresNothingAndNamesUsage()
    {
        _options.QuotaMb = 2;
        await CreateAsync("one.bin", new byte[Megabyte + 100]);

        Result<ArtifactRecord> second = await CreateAsync("two.bin", new byte[Megabyte]);

        Assert.Equal("artifacts.quota_exceeded", second.Error.Code);
        Assert.Contains("2 MB", second.Error.Message, StringComparison.Ordinal);
        Assert.Contains("1 MB used", second.Error.Message, StringComparison.Ordinal);
        Assert.Single(await Store().ListAsync(new ArtifactQuery(), Ct));
        Assert.Single(Directory.GetFiles(Path.Combine(_directory, OwnerA.ToString())));
    }

    [Fact]
    public async Task PerFileLimit_AbortsMidStreamWithoutTempFile()
    {
        _options.MaxUploadMb = 1;
        var content = new MemoryStream(new byte[5 * Megabyte]);

        Result<ArtifactRecord> created = await Store().CreateAsync(new ArtifactDraft("big.bin", ArtifactSource.Upload), content, Ct);

        Assert.Equal("artifacts.too_large", created.Error.Code);
        Assert.True(content.Position < content.Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_directory, OwnerA.ToString())));
        Assert.Empty(await Store().ListAsync(new ArtifactQuery(), Ct));
    }

    [Fact]
    public async Task ConcurrentWrites_OnlyOnePassesTheQuota()
    {
        _options.QuotaMb = 2;

        Result<ArtifactRecord>[] results = await Task.WhenAll(
            Task.Run(() => CreateAsync("one.bin", new byte[(Megabyte * 3) / 2]), Ct),
            Task.Run(() => CreateAsync("two.bin", new byte[(Megabyte * 3) / 2]), Ct));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.IsFailure && result.Error.Code == "artifacts.quota_exceeded");
    }

    [Fact]
    public async Task FindBySource_NewestMatchOfTheOwner()
    {
        ArtifactRecord older = (await Store().CreateAsync(new ArtifactDraft("marshal-pr-42.diff", ArtifactSource.Github, "marshal/pr/42@abc", Tainted: true), new MemoryStream("+a\n"u8.ToArray()), Ct)).Value;
        _time.Advance(TimeSpan.FromMinutes(1));
        ArtifactRecord newer = (await Store().CreateAsync(new ArtifactDraft("marshal-pr-42.diff", ArtifactSource.Github, "marshal/pr/42@abc", Tainted: true), new MemoryStream("+b\n"u8.ToArray()), Ct)).Value;

        ArtifactRecord? found = await Store().FindBySourceAsync(ArtifactSource.Github, "marshal/pr/42@abc", Ct);

        Assert.Equal(newer.Id, found?.Id);
        Assert.NotEqual(older.Id, newer.Id);
        Assert.Equal((ArtifactSource.Github, true), (found!.Source, found.Tainted));
        Assert.Null(await Store().FindBySourceAsync(ArtifactSource.Github, "marshal/pr/43@abc", Ct));
        Assert.Null(await Store().FindBySourceAsync(ArtifactSource.Executor, "marshal/pr/42@abc", Ct));
        _database.Accessor.Current = Text(OwnerB);
        Assert.Null(await Store().FindBySourceAsync(ArtifactSource.Github, "marshal/pr/42@abc", Ct));
    }

    [Fact]
    public async Task GithubSource_ListedBySource()
    {
        await Store().CreateAsync(new ArtifactDraft("homelab-run-7-build.log", ArtifactSource.Github, "homelab/job/7", Tainted: true), new MemoryStream("x\n"u8.ToArray()), Ct);

        ArtifactRecord listed = Assert.Single(await Store().ListAsync(new ArtifactQuery(Source: ArtifactSource.Github), Ct));

        Assert.Equal(ArtifactKind.Code, listed.Kind);
    }

    private Task<Result<ArtifactRecord>> CreateAsync(string name, byte[] content, ArtifactSource source = ArtifactSource.Upload, bool tainted = false) =>
        Store().CreateAsync(new ArtifactDraft(name, source, Tainted: tainted), new MemoryStream(content), Ct);

    private FileArtifactStore Store() =>
        new(_database.Factory, _database.Accessor, new ArtifactKindDetector(), new ArtifactDirectory(_directory), Options.Create(_options), _live, _time);
}
