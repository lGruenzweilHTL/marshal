namespace Marshal.Integrations.CalDav;

/// <summary>
/// The <c>calendar.caldav</c> section of <c>marshal.yaml</c>: CalDAV accounts of the instance owner (Nextcloud,
/// Radicale, Baïkal, Fastmail, iCloud with an app-specific password). Changes need a restart.
/// </summary>
public sealed class CalDavOptions
{
    /// <summary>Configuration section.</summary>
    public const string Section = "calendar:caldav";

    /// <summary>Accounts by id (<c>[a-z][a-z0-9_]*</c>); calendars are addressed as <c>caldav:&lt;id&gt;/&lt;calendar&gt;</c>.</summary>
    public Dictionary<string, CalDavAccountOptions> Accounts { get; set; } = [];

    internal bool IsConfigured => Accounts.Count > 0;
}

/// <summary>One CalDAV account.</summary>
public sealed class CalDavAccountOptions
{
    /// <summary>The DAV URL; calendars are discovered from it (principal, then calendar home set).</summary>
    public Uri? Url { get; set; }

    public string Username { get; set; } = "";

    /// <summary>
    /// Name of the secret holding the password or app password. The client reads it per request, so a rotated secret
    /// needs no restart and a missing one only raises a health warning.
    /// </summary>
    public string PasswordSecret { get; set; } = "";

    /// <summary>Calendar names (last path segment of the collection) to use; all found calendars when not set.</summary>
    public List<string>? Calendars { get; set; }

    /// <summary>Minutes between change polls (1-60).</summary>
    public int PollMinutes { get; set; } = 5;
}
