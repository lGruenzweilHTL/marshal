namespace Marshal.Abstractions.Execution;

/// <summary>
/// How the user entered the turn. Drives output mode, latency cap and advisor visibility.
/// </summary>
public enum InputMode
{
    /// <summary>Typed input (web chat, API, typed Assist).</summary>
    Text,

    /// <summary>Spoken input that went through speech-to-text.</summary>
    Voice,
}
