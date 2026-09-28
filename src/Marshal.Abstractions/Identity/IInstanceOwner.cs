namespace Marshal.Abstractions.Identity;

/// <summary>
/// The instance owner (the first user, created on first start).
/// </summary>
public interface IInstanceOwner
{
    Task<OwnerId> GetAsync(CancellationToken cancellationToken);
}
