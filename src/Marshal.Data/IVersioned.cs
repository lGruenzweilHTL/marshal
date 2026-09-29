namespace Marshal.Data;

/// <summary>
/// A row with an optimistic concurrency token. <see cref="MarshalDbContext"/> increments it on every update, so a
/// writer holding an older version fails with <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>.
/// </summary>
public interface IVersioned
{
    long RowVersion { get; set; }
}
