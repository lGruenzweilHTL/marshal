using Marshal.Abstractions.Memory;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Marshal.Data.Persistence;

/// <summary>
/// Stores a <see cref="ChunkId"/> as its string.
/// </summary>
internal sealed class ChunkIdConverter() : ValueConverter<ChunkId, string>(id => id.Value, value => ChunkId.From(value));
