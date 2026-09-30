using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Core.Configuration;
using Marshal.Tasks.Maintenance;
using Marshal.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Maintenance;

public sealed class BackupJobTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 3, 0, 0, TimeSpan.Zero));
    private readonly string _data = Directory.CreateTempSubdirectory("marshal-backup-").FullName;
    private TestDatabase _database = null!;
    private RecordingExporter _exporter = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Backups => Path.Combine(_data, "backups");

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
        _exporter = new RecordingExporter(_database.Accessor);
        Directory.CreateDirectory(Path.Combine(_data, "keys"));
        await File.WriteAllTextAsync(Path.Combine(_data, "keys", "key-1.xml"), "<key id=\"1\" />", Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_data, recursive: true);
    }

    [Fact]
    public async Task Run_DatabaseCopyKeysAndMemoryExportOfEveryOwner_InADatedFolder()
    {
        Result<string> result = await Job().RunAsync(Ct);

        string folder = Path.Combine(Backups, "20260930-030000");
        Assert.True(result.IsSuccess);
        await using (var copy = new SqliteConnection($"Data Source={Path.Combine(folder, "marshal.db")};Mode=ReadOnly;Pooling=false"))
        {
            await copy.OpenAsync(Ct);
            using SqliteCommand command = copy.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name = 'agent_tasks'";
            Assert.Equal(1L, await command.ExecuteScalarAsync(Ct));
        }

        Assert.Equal("<key id=\"1\" />", await File.ReadAllTextAsync(Path.Combine(folder, "keys", "key-1.xml"), Ct));
        Assert.Equal($"export of {OwnerA}\n", await File.ReadAllTextAsync(Path.Combine(folder, $"memory-{OwnerA}.ndjson"), Ct));
        Assert.Equal($"export of {OwnerB}\n", await File.ReadAllTextAsync(Path.Combine(folder, $"memory-{OwnerB}.ndjson"), Ct));
        Assert.Equal(OwnerA, _database.Accessor.Current!.OwnerId);
    }

    [Fact]
    public async Task Run_KeepsTheNewestCopies_DeletesOnlyOlderBackups()
    {
        foreach (string old in (string[])["20260926-030000", "20260927-030000", "20260928-030000"])
            Directory.CreateDirectory(Path.Combine(Backups, old));
        Directory.CreateDirectory(Path.Combine(Backups, "manual-copy"));

        await Job(copies: 3).RunAsync(Ct);

        Assert.Equal(["20260927-030000", "20260928-030000", "20260930-030000", "manual-copy"],
            Directory.GetDirectories(Backups).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Run_NoKeysDirectory_StillBacksUp()
    {
        Directory.Delete(Path.Combine(_data, "keys"), recursive: true);

        Result<string> result = await Job().RunAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(Path.Combine(Backups, "20260930-030000", "marshal.db")));
    }

    private BackupJob Job(int copies = 7) => new(
        _database.Factory,
        new MemoryExportJob(new FixedDirectory(), _exporter, _database.Accessor),
        Options.Create(new PathsOptions { Data = _data }),
        Options.Create(new MaintenanceOptions { BackupCopies = copies }),
        _time,
        NullLogger<BackupJob>.Instance);

    private sealed class FixedDirectory : IUserDirectory
    {
        public Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnerId>>([OwnerA, OwnerB]);
    }

    // Writes which owner's context the export ran in.
    private sealed class RecordingExporter(IExecutionContextAccessor context) : IMemoryExporter
    {
        public async Task ExportAsync(Stream output, CancellationToken cancellationToken) =>
            await output.WriteAsync(Encoding.UTF8.GetBytes($"export of {context.Current!.OwnerId}\n"), cancellationToken);

        public Task<Result<MemoryImportReport>> ImportAsync(Stream input, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
