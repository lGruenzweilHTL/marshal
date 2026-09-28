using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Marshal.Core.Configuration;

/// <summary>
/// One file system watcher for a whole directory tree, shared by every hot-reloaded file in it (one inotify
/// instance per process instead of one per file). A directory that does not exist is not watched.
/// </summary>
public sealed class ConfigDirectoryWatcher : IConfigWatcher, IDisposable
{
    private readonly PhysicalFileProvider? _provider;

    public ConfigDirectoryWatcher(string directory)
    {
        if (Directory.Exists(directory))
            _provider = new PhysicalFileProvider(Path.GetFullPath(directory));
    }

    public IChangeToken Watch(string filter) => _provider?.Watch(filter) ?? NullChangeToken.Singleton;

    public void Dispose() => _provider?.Dispose();
}
