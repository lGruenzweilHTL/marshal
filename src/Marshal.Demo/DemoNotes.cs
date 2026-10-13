namespace Marshal.Demo;

/// <summary>The simulated notes repository: Markdown files by repository-relative path.</summary>
public sealed class DemoNotes
{
    internal DemoNotes(DateTimeOffset started)
    {
        Files = DemoResources.List("notes").ToDictionary(path => path, path => DemoResources.Text($"notes/{path}"));
        // Older notes first: a stable, plausible modification time per file.
        ModifiedAt = Files.Keys.Select((path, index) => (path, index)).ToDictionary(item => item.path, item => started.AddDays(-3 - (item.index * 5)));
    }

    public IReadOnlyDictionary<string, string> Files { get; }

    public IReadOnlyDictionary<string, DateTimeOffset> ModifiedAt { get; }
}
