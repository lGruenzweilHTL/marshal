using Marshal.Core.Configuration;
using Microsoft.Extensions.Primitives;

namespace Marshal.Core.Tests.Configuration;

public sealed class ConfigDirectoryWatcherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("marshal-watch-").FullName;

    [Fact]
    public async Task Watch_FileInSubdirectoryCreatedLater_Fires()
    {
        using var watcher = new ConfigDirectoryWatcher(_root);
        var fired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IChangeToken token = watcher.Watch("mcp/*.yaml");
        using IDisposable registration = token.RegisterChangeCallback(_ => fired.TrySetResult(), null);

        Directory.CreateDirectory(Path.Combine(_root, "mcp"));
        await File.WriteAllTextAsync(Path.Combine(_root, "mcp", "ha.yaml"), "id: ha\n", TestContext.Current.CancellationToken);

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Watch_MissingDirectory_NeverFires()
    {
        using var watcher = new ConfigDirectoryWatcher(Path.Combine(_root, "missing"));

        Assert.False(watcher.Watch("*.yaml").ActiveChangeCallbacks);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
