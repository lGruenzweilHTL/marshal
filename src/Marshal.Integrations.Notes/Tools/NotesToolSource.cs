using Marshal.Abstractions.Notes;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.Notes.Tools;

/// <summary>The <c>notes.*</c> tools (a built-in source). Read-only.</summary>
public sealed class NotesToolSource(INotesSource source, INotesIndex index) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = NotesTools.Create(source, index);

    public string Name => "notes";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("notes", "The user's notes: searching, reading and writing Markdown notes")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
