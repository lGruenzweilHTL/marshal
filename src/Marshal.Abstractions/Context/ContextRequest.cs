using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Context;

/// <summary>
/// Input for building the model context of a run.
/// </summary>
/// <param name="Context">Execution context (owner, input mode, language, satellite, area).</param>
/// <param name="UserText">Text of the current user turn, if any (used by retrieval contributors).</param>
public sealed record ContextRequest(MarshalExecutionContext Context, string? UserText)
{
    /// <summary>
    /// Why the context looks the way it does (active projects, retrieved memory), recorded with the context-build step
    /// for the web UI. Never part of the prompt.
    /// </summary>
    public ContextNotes Notes { get; } = new();
}

/// <summary>A fact about a context build, shown in the run detail.</summary>
/// <param name="Kind">What it is about, e.g. <c>project</c> or <c>memory</c>.</param>
/// <param name="Label">Name of the item (project name, chunk title).</param>
/// <param name="Detail">Why or how, e.g. <c>alias</c>, <c>preference via vector</c>.</param>
/// <param name="Score">Score, if any.</param>
public sealed record ContextNote(string Kind, string Label, string? Detail = null, double? Score = null);

/// <summary>Notes collected by the contributors of one context build.</summary>
public sealed class ContextNotes
{
    private readonly List<ContextNote> _notes = [];
    private readonly Lock _lock = new();

    /// <summary>Notes in the order they were added.</summary>
    public IReadOnlyList<ContextNote> All
    {
        get
        {
            lock (_lock)
            {
                return [.. _notes];
            }
        }
    }

    public void Add(ContextNote note)
    {
        lock (_lock)
        {
            _notes.Add(note);
        }
    }
}
