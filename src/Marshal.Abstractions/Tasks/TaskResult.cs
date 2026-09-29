namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Result of a task run: <paramref name="Speech"/> is short and speakable, <paramref name="Body"/> is markdown.
/// </summary>
public sealed record TaskResult(string Speech, string Body);
