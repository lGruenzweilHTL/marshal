using Marshal.Abstractions;
using Marshal.Abstractions.Notes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Notes.Tests.Source;

public sealed class GitNotesSourceTests : IDisposable
{
    private readonly List<string> _cleanup = [];
    private readonly TestRepository _repository = TestRepository.Create()
        .Write("README.md", "# Notes\nStart here.")
        .Write("projects/marshal.md", "# Marshal\n## Voice\nHA Assist first.")
        .Write("projects/budget.txt", "not markdown")
        .Write("Reisen/Wien.md", "# Wien\nPrater.")
        .Commit();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_MarkdownOnlySortedWithForwardSlashes()
    {
        IReadOnlyList<NoteFile> files = (await Local().ListAsync(null, Ct)).Value;

        Assert.Equal(["README.md", "Reisen/Wien.md", "projects/marshal.md"], files.Select(file => file.Path));
        Assert.All(files, file => Assert.True(file.Size > 0));
    }

    [Fact]
    public async Task List_Folder()
    {
        IReadOnlyList<NoteFile> files = (await Local().ListAsync("projects", Ct)).Value;

        Assert.Equal(["projects/marshal.md"], files.Select(file => file.Path));
    }

    [Fact]
    public async Task Read_MarkdownFile()
    {
        Assert.Equal("# Marshal\n## Voice\nHA Assist first.", (await Local().ReadAsync("projects/marshal.md", Ct)).Value);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData("projects/../../outside.md")]
    [InlineData("projects/budget.txt")]
    [InlineData(".git/config")]
    [InlineData("")]
    public async Task Read_OutsideTheRepositoryOrNotMarkdown_PathInvalid(string path)
    {
        Result<string> read = await Local().ReadAsync(path, Ct);

        Assert.Equal("notes.path_invalid", read.Error.Code);
    }

    [Fact]
    public async Task List_FolderOutsideTheRepository_PathInvalid()
    {
        Assert.Equal("notes.path_invalid", (await Local().ListAsync("../", Ct)).Error.Code);
    }

    [Fact]
    public async Task Symlinks_AreNeitherListedNorRead()
    {
        string elsewhere = Directory.CreateTempSubdirectory("marshal-outside-").FullName;
        _cleanup.Add(elsewhere);
        string outside = Path.Combine(elsewhere, "secret.md");
        File.WriteAllText(outside, "outside the repository");
        File.CreateSymbolicLink(Path.Combine(_repository.Path, "link.md"), outside);
        Directory.CreateSymbolicLink(Path.Combine(_repository.Path, "linked"), elsewhere);

        GitNotesSource source = Local();

        Assert.DoesNotContain((await source.ListAsync(null, Ct)).Value, file => file.Path.StartsWith("link", StringComparison.Ordinal));
        Assert.Equal("notes.path_invalid", (await source.ReadAsync("link.md", Ct)).Error.Code);
        Assert.Equal("notes.path_invalid", (await source.ReadAsync($"linked/{Path.GetFileName(outside)}", Ct)).Error.Code);
    }

    [Fact]
    public async Task Read_Missing_NotFound()
    {
        Assert.Equal("notes.not_found", (await Local().ReadAsync("projects/missing.md", Ct)).Error.Code);
    }

    [Fact]
    public async Task Remote_ClonedOnFirstSyncThenPulled()
    {
        string clone = TestRepository.EmptyDirectory();
        _cleanup.Add(clone);
        GitNotesSource source = Remote(clone);

        Assert.True((await source.SyncAsync(Ct)).IsSuccess);
        Assert.Equal("# Wien\nPrater.", (await source.ReadAsync("Reisen/Wien.md", Ct)).Value);

        _repository.Write("Reisen/Graz.md", "# Graz\nSchlossberg.").Delete("README.md").Commit();
        Assert.True((await source.SyncAsync(Ct)).IsSuccess);

        Assert.Equal(["Reisen/Graz.md", "Reisen/Wien.md", "projects/marshal.md"], (await source.ListAsync(null, Ct)).Value.Select(file => file.Path));
    }

    [Fact]
    public async Task Remote_Branch()
    {
        _repository.Git("checkout", "--quiet", "-b", "notes");
        _repository.Write("branch-only.md", "# Branch").Commit();
        _repository.Git("checkout", "--quiet", "main");
        string clone = TestRepository.EmptyDirectory();
        _cleanup.Add(clone);
        GitNotesSource source = Remote(clone, branch: "notes");

        await source.SyncAsync(Ct);

        Assert.Equal("# Branch", (await source.ReadAsync("branch-only.md", Ct)).Value);
    }

    [Fact]
    public async Task Remote_TargetIsAnUnrelatedDirectory_FailsWithoutTouchingIt()
    {
        string target = Directory.CreateTempSubdirectory("marshal-notes-").FullName;
        _cleanup.Add(target);
        File.WriteAllText(Path.Combine(target, "keep.md"), "mine");

        Result synced = await Remote(target).SyncAsync(Ct);

        Assert.Equal("notes.sync_failed", synced.Error.Code);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(target, "keep.md")));
    }

    [Fact]
    public async Task Remote_Unreachable_SyncFailed()
    {
        string clone = TestRepository.EmptyDirectory();
        _cleanup.Add(clone);
        var source = new GitNotesSource(Options.Create(new NotesOptions { Remote = "file:///nonexistent/notes.git" }), new NotesDirectory(clone), NullLogger<GitNotesSource>.Instance);

        Assert.Equal("notes.sync_failed", (await source.SyncAsync(Ct)).Error.Code);
    }

    [Fact]
    public void Configured_WithRemoteOrExistingDirectory()
    {
        Assert.True(Local().IsConfigured);
        Assert.True(Remote(TestRepository.EmptyDirectory()).IsConfigured);
        Assert.False(new GitNotesSource(Options.Create(new NotesOptions()), new NotesDirectory(TestRepository.EmptyDirectory()), NullLogger<GitNotesSource>.Instance).IsConfigured);
    }

    public void Dispose()
    {
        _repository.Dispose();
        foreach (string path in _cleanup)
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
    }

    private GitNotesSource Local() =>
        new(Options.Create(new NotesOptions()), new NotesDirectory(_repository.Path), NullLogger<GitNotesSource>.Instance);

    private GitNotesSource Remote(string clone, string? branch = null) =>
        new(Options.Create(new NotesOptions { Remote = new Uri(_repository.Path).AbsoluteUri, Branch = branch }), new NotesDirectory(clone), NullLogger<GitNotesSource>.Instance);
}
