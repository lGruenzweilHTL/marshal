using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.AiTasks;

/// <summary>
/// Limits for AI tasks from Home Assistant automations (<c>aiTasks</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class AiTaskOptions
{
    public const string Section = "aiTasks";

    /// <summary>Upper bound for <see cref="MaxAttachmentBytes"/>: 25 MB.</summary>
    public const int MaxAttachmentBytesLimit = 25 * 1024 * 1024;

    /// <summary>A run still working after this many seconds is cancelled (504).</summary>
    [Range(1, 3600, ErrorMessage = "aiTasks.timeoutSeconds must be between {1} and {2}.")]
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Most attachments one task may carry.</summary>
    [Range(1, 32, ErrorMessage = "aiTasks.maxAttachments must be between {1} and {2}.")]
    public int MaxAttachments { get; set; } = 4;

    /// <summary>Largest size of one attachment in bytes.</summary>
    [Range(1, MaxAttachmentBytesLimit, ErrorMessage = "aiTasks.maxAttachmentBytes must be between {1} and {2}.")]
    public int MaxAttachmentBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>Request body limit: every attachment at full size plus 64 KB for the task part and multipart framing.</summary>
    public long RequestBodyLimit => ((long)MaxAttachments * MaxAttachmentBytes) + (64 * 1024);
}
