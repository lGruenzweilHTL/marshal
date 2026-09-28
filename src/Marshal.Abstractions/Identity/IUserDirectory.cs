namespace Marshal.Abstractions.Identity;

/// <summary>
/// Lists the users of the instance (for per-owner maintenance jobs).
/// </summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken);
}
