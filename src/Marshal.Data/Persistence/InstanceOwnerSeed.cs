namespace Marshal.Data.Persistence;

/// <summary>
/// Profile of the instance owner created on first start.
/// </summary>
/// <param name="DisplayName">Name the system prompt uses for the user.</param>
/// <param name="Language">Preferred language (<c>de</c> or <c>en</c>).</param>
/// <param name="TimeZone">IANA time zone id.</param>
public sealed record InstanceOwnerSeed(string DisplayName, string Language, string TimeZone);
