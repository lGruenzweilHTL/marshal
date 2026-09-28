using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Persistence;

/// <summary>
/// The one place that configures the SQLite provider for Marshal.
/// </summary>
public static class SqliteOptions
{
    /// <summary>Uses SQLite on a connection, with snake_case naming.</summary>
    public static DbContextOptionsBuilder<MarshalDbContext> UseMarshalSqlite(this DbContextOptionsBuilder<MarshalDbContext> builder, DbConnection connection)
    {
        builder.UseSqlite(connection).UseSnakeCaseNamingConvention();
        return builder;
    }

    /// <summary>Uses SQLite with a connection string, with snake_case naming.</summary>
    public static DbContextOptionsBuilder<MarshalDbContext> UseMarshalSqlite(this DbContextOptionsBuilder<MarshalDbContext> builder, string connectionString)
    {
        builder.UseSqlite(connectionString).UseSnakeCaseNamingConvention();
        return builder;
    }

    /// <summary>Uses SQLite with a connection string, with snake_case naming (dependency injection).</summary>
    public static DbContextOptionsBuilder UseMarshalSqlite(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlite(connectionString).UseSnakeCaseNamingConvention();
}
