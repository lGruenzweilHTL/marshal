using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Agent;

/// <summary>
/// The redaction rule without recording settings: only tools flagged sensitive. The host replaces it with the
/// recorder's rule.
/// </summary>
public sealed class SensitiveToolRedaction : IToolRedaction
{
    public bool Redacts(ToolId tool, bool sensitive) => sensitive;
}
