using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.Sessions;

/// <summary>
/// Session settings (<c>sessions</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class SessionOptions
{
    /// <summary>A session without a turn for this long is closed.</summary>
    [Range(typeof(TimeSpan), "00:00:30", "1.00:00:00")]
    public TimeSpan IdleTtl { get; set; } = TimeSpan.FromMinutes(5);
}
