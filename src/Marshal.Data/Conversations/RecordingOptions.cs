namespace Marshal.Data.Conversations;

/// <summary>
/// Run recording settings (<c>recording</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class RecordingOptions
{
    /// <summary>Tool id globs whose results are always stored as references only (in addition to tools flagged sensitive).</summary>
    public List<string> RedactTools { get; set; } = [];

    /// <summary>Tool id globs whose results are stored in full even when the tool is flagged sensitive.</summary>
    public List<string> KeepContentTools { get; set; } = [];

    /// <summary>Longer contents are truncated in run step payloads.</summary>
    public int MaxContentLength { get; set; } = 8000;
}
