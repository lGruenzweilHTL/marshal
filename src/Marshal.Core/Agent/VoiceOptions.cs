using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.Agent;

/// <summary>
/// Voice turn behavior (<c>voice</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class VoiceOptions
{
    /// <summary>A tool expected to take longer than this gets its spoken filler line first.</summary>
    [Range(1, 60_000, ErrorMessage = "voice.fillerThresholdMs must be between {1} and {2}.")]
    public int FillerThresholdMs { get; set; } = 700;
}
