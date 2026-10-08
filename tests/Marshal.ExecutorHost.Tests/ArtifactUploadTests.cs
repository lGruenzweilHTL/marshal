using System.Net.Http.Json;
using Marshal.ExecutorHost.Callbacks;

namespace Marshal.ExecutorHost.Tests;

public sealed class ArtifactUploadTests : IDisposable
{
    private readonly HostFactory _host = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Completed_UploadsEachArtifactFileBeforeTheCompletion()
    {
        _host.Sandbox.Work = directory =>
        {
            Write(directory, ".marshal/artifacts/bracket.stl", "solid bracket");
            Write(directory, ".marshal/artifacts/renders/front.png", "png");
        };

        await StartAsync();
        ExecutorCallback completed = await _host.Callbacks.WaitForAsync("completed");

        Assert.Equal(["bracket.stl", "front.png"], _host.Callbacks.Uploads.Select(upload => upload.Name).Order(StringComparer.Ordinal));
        (Uri url, string token, _, byte[] content) = _host.Callbacks.Uploads.Single(upload => upload.Name == "bracket.stl");
        Assert.Equal((new Uri("http://marshal.test/api/v1/executor/callbacks/abc"), "callback-token"), (url, token));
        Assert.Equal("solid bracket"u8.ToArray(), content);
        Assert.Null(completed.Message);
    }

    [Fact]
    public async Task SymbolicLinksAndEscapes_SkippedAndNamedInTheNotes()
    {
        string secret = Path.Combine(_host.Root, "secret.txt");
        File.WriteAllText(secret, "host secret");
        _host.Sandbox.Work = directory =>
        {
            string artifacts = Path.Combine(directory, ".marshal", "artifacts");
            Directory.CreateDirectory(artifacts);
            File.CreateSymbolicLink(Path.Combine(artifacts, "leak.txt"), secret);
            Directory.CreateSymbolicLink(Path.Combine(artifacts, "outside"), _host.Root);
            Write(directory, ".marshal/artifacts/ok.md", "# ok");
        };

        await StartAsync();
        ExecutorCallback completed = await _host.Callbacks.WaitForAsync("completed");

        Assert.Equal("ok.md", Assert.Single(_host.Callbacks.Uploads).Name);
        Assert.Contains("leak.txt", completed.Message, StringComparison.Ordinal);
        Assert.Contains("outside", completed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("host secret", completed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusedUpload_NamedWithTheReasonInTheNotes()
    {
        _host.Callbacks.Refusals["huge.stl"] = "The file is larger than the limit of 200 MB.";
        _host.Sandbox.Work = directory =>
        {
            Write(directory, ".marshal/artifacts/huge.stl", "solid huge");
            Write(directory, ".marshal/artifacts/small.stl", "solid small");
        };

        await StartAsync();
        ExecutorCallback completed = await _host.Callbacks.WaitForAsync("completed");

        Assert.Equal("small.stl", Assert.Single(_host.Callbacks.Uploads).Name);
        Assert.Contains("huge.stl: The file is larger than the limit of 200 MB.", completed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoArtifactFolder_NothingUploaded()
    {
        await StartAsync();
        ExecutorCallback completed = await _host.Callbacks.WaitForAsync("completed");

        Assert.Empty(_host.Callbacks.Uploads);
        Assert.Null(completed.Message);
    }

    private async Task StartAsync() =>
        await _host.Authorized().PostAsJsonAsync("/sessions", new { repository = "demo", prompt = "Model a bracket.", callbackUrl = "http://marshal.test/api/v1/executor/callbacks/abc", callbackToken = "callback-token" }, Ct);

    private static void Write(string directory, string relative, string content)
    {
        string path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
