using Marshal.Abstractions;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Notes;

namespace Marshal.Integrations.Notes.Tools;

/// <summary><c>note:&lt;path&gt;</c> for <c>agent.extract</c>: the note's Markdown text (taint source <c>notes</c>).</summary>
public sealed class NoteContentResolver(INotesSource source) : IUntrustedContentResolver
{
    public string Kind => "note";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(source.IsConfigured);

    public async Task<Result<UntrustedItem>> ResolveAsync(string id, CancellationToken cancellationToken)
    {
        if (!source.IsConfigured)
            return NotFound(id);

        Result<string> text = await source.ReadAsync(id, cancellationToken);
        return text.IsSuccess ? new UntrustedItem($"{Kind}:{id}", text.Value, "notes") : NotFound(id);
    }

    private static Error NotFound(string id) => new(ExtractionAnswer.NotFoundCode, $"There is no note '{id}'.", ErrorType.NotFound);
}
