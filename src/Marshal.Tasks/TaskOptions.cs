namespace Marshal.Tasks;

/// <summary>
/// The <c>tasks:</c> section of <c>marshal.yaml</c>.
/// </summary>
public sealed class TaskOptions
{
    /// <summary>Task runs that execute at the same time; further due tasks wait in order of their run time.</summary>
    public int Workers { get; set; } = 2;
}
