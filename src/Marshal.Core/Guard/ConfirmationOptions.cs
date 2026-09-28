using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.Guard;

/// <summary>
/// In-conversation confirmation settings (<c>confirmation</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class ConfirmationOptions
{
    /// <summary>How long a pending action can be confirmed.</summary>
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00", ErrorMessage = "confirmation.ttl must be between {1} and {2}.")]
    public TimeSpan Ttl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Timeout of the confirmation judge; on expiry the action is dropped.</summary>
    [Range(typeof(TimeSpan), "00:00:00.5", "00:00:30", ErrorMessage = "confirmation.judgeTimeout must be between {1} and {2}.")]
    public TimeSpan JudgeTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Keywords per language; a configured language replaces its built-in list.</summary>
    public Dictionary<string, List<string>> Keywords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
