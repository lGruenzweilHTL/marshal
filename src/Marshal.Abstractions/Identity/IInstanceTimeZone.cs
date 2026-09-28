namespace Marshal.Abstractions.Identity;

/// <summary>
/// The instance's time zone: new users start with it.
/// </summary>
public interface IInstanceTimeZone
{
    /// <summary>IANA time zone id.</summary>
    Task<string> GetAsync(CancellationToken cancellationToken);
}
