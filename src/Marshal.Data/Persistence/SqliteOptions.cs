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
        // An already open connection (tests' in-memory databases) never raises the opened event.
        if (connection is Microsoft.Data.Sqlite.SqliteConnection { State: System.Data.ConnectionState.Open } open)
        {
            SqliteVectorExtension.Load(open);
            SqliteConnectionSettings.Apply(open);
        }

        builder.UseSqlite(connection).UseSnakeCaseNamingConvention().AddInterceptors(SqliteVectorExtension.Interceptor.Instance, SqliteConnectionSettings.Instance);
        return builder;
    }

    /// <summary>Uses SQLite with a connection string, with snake_case naming.</summary>
    public static DbContextOptionsBuilder<MarshalDbContext> UseMarshalSqlite(this DbContextOptionsBuilder<MarshalDbContext> builder, string connectionString)
    {
        builder.UseSqlite(connectionString).UseSnakeCaseNamingConvention().AddInterceptors(SqliteVectorExtension.Interceptor.Instance, SqliteConnectionSettings.Instance);
        return builder;
    }

    /// <summary>Uses SQLite with a connection string, with snake_case naming (dependency injection).</summary>
    public static DbContextOptionsBuilder UseMarshalSqlite(this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseSqlite(connectionString).UseSnakeCaseNamingConvention().AddInterceptors(SqliteVectorExtension.Interceptor.Instance, SqliteConnectionSettings.Instance);
}
