using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Marshal.ExecutorHost.Callbacks;
using Marshal.ExecutorHost.Sandbox;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.ExecutorHost.Tests;

/// <summary>
/// The executor host with a real git repository (<c>demo</c>), a faked sandbox runtime and recorded callbacks.
/// </summary>
internal sealed class HostFactory : WebApplicationFactory<Program>
{
    public const string Token = "host-token-0123456789";
    public const string MarshalUrl = "http://marshal.test/";

    public HostFactory()
    {
        Root = Directory.CreateTempSubdirectory("executor-host-").FullName;
        Directory.CreateDirectory(Repositories);
        Directory.CreateDirectory(Worktrees);
        File.WriteAllText(TokenFile, Token + "\n");
        string demo = Path.Combine(Repositories, "demo");
        Directory.CreateDirectory(demo);
        Git(demo, "init", "-q", "-b", "main");
        File.WriteAllText(Path.Combine(demo, "README.md"), "# Demo\n");
        Git(demo, "add", "-A");
        Git(demo, "-c", "user.name=Test", "-c", "user.email=test@localhost", "commit", "-q", "-m", "init");
    }

    public string Root { get; }

    public string Repositories => Path.Combine(Root, "repos");

    public string Worktrees => Path.Combine(Root, "worktrees");

    public string TokenFile => Path.Combine(Root, "token");

    public FakeSandbox Sandbox { get; } = new();

    public RecordingCallbacks Callbacks { get; } = new();

    public HttpClient Authorized()
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    public static string Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("executorHost:tokenFile", TokenFile);
        builder.UseSetting("executorHost:repositoriesRoot", Repositories);
        builder.UseSetting("executorHost:worktreesRoot", Worktrees);
        builder.UseSetting("executorHost:marshalUrl", MarshalUrl);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISandboxRuntime>();
            services.AddSingleton<ISandboxRuntime>(Sandbox);
            services.RemoveAll<ICallbackSender>();
            services.AddSingleton<ICallbackSender>(Callbacks);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

/// <summary>Stands in for Docker: edits the worktree, prints stream-json and exits, or waits until released or cancelled.</summary>
internal sealed class FakeSandbox : ISandboxRuntime
{
    public ConcurrentQueue<SandboxRun> Runs { get; } = new();

    /// <summary>When set, runs wait for it before finishing.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public int ExitCode { get; set; }

    /// <summary>Runs in the worktree before the result line (e.g. writes <c>.marshal/artifacts/</c>).</summary>
    public Action<string>? Work { get; set; }

    public bool IsError { get; set; }

    public async Task<int> RunAsync(SandboxRun run, Func<string, CancellationToken, Task> onLine, CancellationToken cancellationToken)
    {
        Runs.Enqueue(run);
        await onLine(JsonSerializer.Serialize(new { type = "system", subtype = "init" }), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(run.WorkDirectory, "feature.txt"), $"change for {run.Name}\n", cancellationToken);
        await onLine(JsonSerializer.Serialize(new { type = "assistant", message = new { content = new[] { new { type = "text", text = "Added feature.txt." } } } }), cancellationToken);
        Work?.Invoke(run.WorkDirectory);
        if (Gate is { } gate)
            await gate.Task.WaitAsync(cancellationToken);
        await onLine(JsonSerializer.Serialize(new { type = "result", subtype = IsError ? "error_during_execution" : "success", is_error = IsError, result = "Added feature.txt with a note.", total_cost_usd = 0.12 }), cancellationToken);
        return ExitCode;
    }
}

internal sealed class RecordingCallbacks : ICallbackSender
{
    public ConcurrentQueue<(Uri Url, string Token, ExecutorCallback Callback)> Sent { get; } = new();

    public ConcurrentQueue<(Uri Url, string Token, string Name, byte[] Content)> Uploads { get; } = new();

    /// <summary>Refusal reasons by file name, like Marshal's size or quota problems.</summary>
    public Dictionary<string, string> Refusals { get; } = [];

    public Task SendAsync(Uri url, string token, ExecutorCallback callback, CancellationToken cancellationToken)
    {
        Sent.Enqueue((url, token, callback));
        return Task.CompletedTask;
    }

    public async Task<string?> UploadAsync(Uri url, string token, string name, Stream content, CancellationToken cancellationToken)
    {
        if (Refusals.TryGetValue(name, out string? reason))
            return reason;

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Uploads.Enqueue((url, token, name, buffer.ToArray()));
        return null;
    }

    public async Task<ExecutorCallback> WaitForAsync(string type)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (Sent.FirstOrDefault(sent => sent.Callback.Type == type) is { Callback: not null } found)
                return found.Callback;
            await Task.Delay(20);
        }

        throw new TimeoutException($"No '{type}' callback.");
    }
}
