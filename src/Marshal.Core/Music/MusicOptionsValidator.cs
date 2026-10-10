using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Music;

/// <summary>Validates speaker names and that each speaker is a <c>media_player</c> entity.</summary>
public sealed partial class MusicOptionsValidator : IValidateOptions<MusicOptions>
{
    public ValidateOptionsResult Validate(string? name, MusicOptions options)
    {
        var failures = new List<string>();
        foreach ((string speaker, string entity) in options.Speakers)
        {
            string path = $"music.speakers.{speaker}";
            if (!SpeakerName().IsMatch(speaker))
                failures.Add($"{path} must be named with lowercase letters, digits and underscores, starting with a letter.");
            else if (entity is null || !entity.StartsWith("media_player.", StringComparison.Ordinal) || entity.Length <= "media_player.".Length)
                failures.Add($"{path} must be a media_player entity id.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SpeakerName();
}
