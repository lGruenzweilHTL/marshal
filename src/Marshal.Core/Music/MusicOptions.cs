namespace Marshal.Core.Music;

/// <summary>The <c>music</c> section of <c>marshal.yaml</c>: Music Assistant players by speaker name.</summary>
public sealed class MusicOptions
{
    /// <summary>Speaker name → Home Assistant <c>media_player</c> entity id of a Music Assistant player.</summary>
    public Dictionary<string, string> Speakers { get; set; } = new(StringComparer.Ordinal);
}
