namespace Marshal.Abstractions.Notes;

/// <summary>Expected failures of the notes integration.</summary>
public static class NotesErrors
{
    public static Error NotConfigured() => new("notes.not_configured", "No notes repository is configured.", ErrorType.Unavailable);

    public static Error PathInvalid(string path) =>
        new("notes.path_invalid", $"'{path}' is not a Markdown file path inside the notes repository.", ErrorType.Validation);

    public static Error NotFound(string path) => new("notes.not_found", $"There is no note '{path}'.", ErrorType.NotFound);

    public static Error SyncFailed(string reason) => new("notes.sync_failed", $"The notes repository could not be updated: {reason}", ErrorType.Unavailable);
}
