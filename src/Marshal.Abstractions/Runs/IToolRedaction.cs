using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// Which tool results are kept out of run records and summaries: tools flagged sensitive and the configured
/// <c>recording.redactTools</c>, minus <c>recording.keepContentTools</c>.
/// </summary>
public interface IToolRedaction
{
    bool Redacts(ToolId tool, bool sensitive);
}
