using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Data.Conversations;

/// <summary>
/// The redaction rule of the <c>recording</c> settings, shared by the recorder and the tool-call summaries.
/// </summary>
public sealed class RecordingToolRedaction(IOptions<RecordingOptions> options) : IToolRedaction
{
    public bool Redacts(ToolId tool, bool sensitive) =>
        (sensitive || options.Value.RedactTools.Any(pattern => ToolGlob.Matches(pattern, tool)))
        && !options.Value.KeepContentTools.Any(pattern => ToolGlob.Matches(pattern, tool));
}
