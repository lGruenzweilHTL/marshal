namespace Marshal.Abstractions.Tools;

/// <summary>
/// Builds <see cref="ToolDescriptor.Fillers"/> in Marshal's two reply languages.
/// </summary>
public static class ToolFillers
{
    /// <summary>No filler lines.</summary>
    public static IReadOnlyDictionary<string, string> None { get; } = new Dictionary<string, string>();

    /// <summary>A German and an English line.</summary>
    public static IReadOnlyDictionary<string, string> Lines(string german, string english) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["de"] = german, ["en"] = english };
}
