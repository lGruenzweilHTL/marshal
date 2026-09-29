using System.Diagnostics;

namespace Marshal.Integrations.Notes.Tests.Source;

/// <summary>A git repository in a temporary directory, written and committed with the git CLI.</summary>
internal sealed class TestRepository : IDisposable
{
    private TestRepository(string path) => Path = path;

    public string Path { get; }

    public static TestRepository Create()
    {
        var repository = new TestRepository(Directory.CreateTempSubdirectory("marshal-notes-").FullName);
        repository.Git("init", "--quiet", "--initial-branch=main");
        repository.Git("config", "user.email", "test@example.com");
        repository.Git("config", "user.name", "Test");
        return repository;
    }

    /// <summary>A directory that does not exist yet, next to the repositories.</summary>
    public static string EmptyDirectory() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "marshal-notes-" + Guid.NewGuid().ToString("N"));

    public TestRepository Write(string relativePath, string content)
    {
        string full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public TestRepository Delete(string relativePath)
    {
        File.Delete(System.IO.Path.Combine(Path, relativePath));
        return this;
    }

    public TestRepository Commit(string message = "update")
    {
        Git("add", "--all");
        Git("commit", "--quiet", "--allow-empty", "-m", message);
        return this;
    }

    public void Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Path, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error}");
    }

    // Deletes links, never what they point to.
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
