using Marshal.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Marshal.Data.Persistence;

/// <summary>
/// Stores a strongly typed id as its Guid.
/// </summary>
internal sealed class GuidIdConverter<TId>() : ValueConverter<TId, Guid>(id => ToGuid(id), value => FromGuid(value))
    where TId : struct, IGuidId<TId>
{
    private static Guid ToGuid(TId id) => id.Value;

    private static TId FromGuid(Guid value) => TId.From(value);
}
