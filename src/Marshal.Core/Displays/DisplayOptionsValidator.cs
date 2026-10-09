using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Displays;

/// <summary>
/// Validates display names, entities, the link lifetime and the base URL's form. Whether a base URL exists at all
/// depends on <c>notifications.publicUrl</c> and is checked by the host.
/// </summary>
public sealed partial class DisplayOptionsValidator : IValidateOptions<DisplayOptions>
{
    public ValidateOptionsResult Validate(string? name, DisplayOptions options)
    {
        var failures = new List<string>();
        if (options.LinkMinutes is < 5 or > 1440)
            failures.Add("displays.linkMinutes must be between 5 and 1440.");
        if (options.MediaBaseUrl is { } url && !IsHttpUrl(url))
            failures.Add("displays.mediaBaseUrl must be an absolute http or https URL.");

        foreach ((string display, DisplaySurfaceOptions surface) in options.Surfaces)
        {
            string path = $"displays.surfaces.{display}";
            if (!DisplayName().IsMatch(display))
                failures.Add($"{path} must be named with lowercase letters, digits and underscores, starting with a letter.");
            else if (!surface.Entity.StartsWith("media_player.", StringComparison.Ordinal) || surface.Entity.Length <= "media_player.".Length)
                failures.Add($"{path}.entity must be a media_player entity id.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    internal static bool IsHttpUrl(Uri url) => url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex DisplayName();
}
