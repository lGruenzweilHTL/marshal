namespace Marshal.Core.Tools;

/// <summary>
/// Why a tool namespace was loaded, as recorded in the run.
/// </summary>
public static class LoadReasons
{
    public const string Core = "core";
    public const string Session = "session";
    public const string Hint = "hint";
    public const string Retrieval = "retrieval";
    public const string FindTools = "find_tools";
    public const string Call = "call";

    /// <summary>Kind of the context notes that list loaded namespaces.</summary>
    public const string NoteKind = "tool_namespace";
}
