using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Identity;

namespace Marshal.Core.Context;

/// <summary>
/// Reply language and the local date and time, in the owner's time zone and formatted in the reply language.
/// </summary>
public sealed class TimeLocaleContributor(IUserProfileProvider profiles, TimeProvider timeProvider) : IContextContributor
{
    public ContextStability Stability => ContextStability.Turn;

    public int Order => 200;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        Result<UserProfile> profile = await profiles.GetAsync(request.Context.OwnerId, cancellationToken);
        TimeZoneInfo zone = FindZone(profile.IsSuccess ? profile.Value.TimeZone : null);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), zone);

        bool german = request.Context.Language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        var culture = CultureInfo.GetCultureInfo(german ? "de-DE" : "en-US");
        string languageName = german ? "German" : "English";
        string date = local.ToString("D", culture);
        string time = local.ToString(german ? "HH:mm" : "h:mm tt", culture);
        return $"Reply in {languageName}. Format dates and numbers for {languageName}.\nLocal time: {date}, {time} ({zone.Id}).";
    }

    private static TimeZoneInfo FindZone(string? id)
    {
        if (id is null)
            return TimeZoneInfo.Utc;

        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Utc;
    }
}
