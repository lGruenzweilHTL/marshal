using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;

namespace Marshal.Core.AiTasks;

/// <summary>
/// Runs one AI task from a Home Assistant automation as a headless agent run.
/// </summary>
public interface IAiTaskRunner
{
    /// <summary>
    /// Runs the task. Fails with <see cref="AiTaskErrors"/> codes (timeout, daily cap, no vision model, failed run) or
    /// <see cref="StructuredOutput.InvalidOutputCode"/>.
    /// </summary>
    Task<Result<AiTaskResult>> RunAsync(AiTaskRequest request, CancellationToken cancellationToken);
}

/// <summary>An AI task.</summary>
/// <param name="Owner">Whose run it is (the token's owner).</param>
/// <param name="TaskName">HA's task name; the run's trigger is <c>ai_task:&lt;name&gt;</c>.</param>
/// <param name="Instructions">What to do; trusted like a typed turn.</param>
/// <param name="Structure">JSON Schema of the result; null = text.</param>
/// <param name="Language">Reply language (e.g. <c>de</c>, <c>en-GB</c>); null or unsupported = the owner's preference.</param>
/// <param name="Attachments">Images to look at.</param>
public sealed record AiTaskRequest(
    OwnerId Owner,
    string TaskName,
    string Instructions,
    JsonElement? Structure,
    string? Language,
    IReadOnlyList<AiTaskAttachment> Attachments);

/// <summary>An attached image.</summary>
public sealed record AiTaskAttachment(string MediaType, ReadOnlyMemory<byte> Data);

/// <summary>The task's answer: a JSON string without structure, an object matching it otherwise.</summary>
public sealed record AiTaskResult(RunId RunId, JsonElement Data);

/// <summary>Error codes of AI tasks.</summary>
public static class AiTaskErrors
{
    public const string TimeoutCode = "ai_task.timeout";

    public const string DailyCapCode = "ai_task.daily_cap";

    public const string NoVisionModelCode = "ai_task.no_vision_model";

    public const string FailedCode = "ai_task.failed";
}
