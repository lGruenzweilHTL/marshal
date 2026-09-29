using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Marshal.Data.Persistence;

/// <summary>
/// Per-connection settings applied whenever EF Core opens a connection. The busy timeout lets a writer wait for
/// another connection's write lock (task workers, web requests and background services write concurrently) instead
/// of failing at once with "database is locked".
/// </summary>
internal sealed class SqliteConnectionSettings : DbConnectionInterceptor
{
    public const int BusyTimeoutMilliseconds = 5000;

    public static SqliteConnectionSettings Instance { get; } = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => Apply(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    public static void Apply(DbConnection connection)
    {
        if (connection is not SqliteConnection)
            return;

        using DbCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA busy_timeout = {BusyTimeoutMilliseconds}";
        command.ExecuteNonQuery();
    }
}
