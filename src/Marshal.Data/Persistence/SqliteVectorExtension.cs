using System.Data.Common;
using Marshal.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Marshal.Data.Persistence;

/// <summary>
/// The <c>sqlite-vec</c> extension (<c>vec0</c>, shipped as a native asset of the <c>sqlite-vec</c> package) that
/// backs memory vector search. It is loaded into every connection; a missing binary does not break the rest of the
/// database, it shows as a health error instead.
/// </summary>
public static class SqliteVectorExtension
{
    private const string ExtensionFile = "vec0";

    /// <summary>Loads the extension into an open connection.</summary>
    public static void Load(SqliteConnection connection)
    {
        connection.EnableExtensions(true);
        try
        {
            connection.LoadExtension(ExtensionFile);
        }
        finally
        {
            connection.EnableExtensions(false);
        }
    }

    /// <summary>The loaded extension's version, or why it is not available on this context's connection.</summary>
    public static async Task<Result<string>> ProbeAsync(DbContext db, CancellationToken cancellationToken)
    {
        DbConnection connection = db.Database.GetDbConnection();
        bool opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
            await db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT vec_version()";
            return (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "";
        }
        catch (SqliteException exception)
        {
            return new Error("memory.vector_extension_missing", $"sqlite-vec is not loaded ({exception.SqliteErrorCode}): {exception.Message}", ErrorType.Failure);
        }
        finally
        {
            if (opened)
                await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>Loads the extension whenever EF Core opens a connection.</summary>
    internal sealed class Interceptor : DbConnectionInterceptor
    {
        public static Interceptor Instance { get; } = new();

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => TryLoad(connection);

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            TryLoad(connection);
            return Task.CompletedTask;
        }

        // Without the binary only vector search fails; the probe reports it through /health.
        private static void TryLoad(DbConnection connection)
        {
            if (connection is not SqliteConnection sqlite)
                return;

            try
            {
                Load(sqlite);
            }
            catch (SqliteException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
