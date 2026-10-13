using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Demo.Seeding;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Demo.Tests;

public sealed class DemoSeedServiceTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero));
    private readonly RecordingSeeder _seeder = new();
    private readonly SettableOwner _owner = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_OwnerExists_SeedsOnce()
    {
        _owner.Owner = TestExecutionContext.OwnerA;

        await RunAsync();
        await RunAsync();

        Assert.Equal([TestExecutionContext.OwnerA], _seeder.Seeded);
    }

    [Fact]
    public async Task Start_DemoOff_SeedsNothing()
    {
        _owner.Owner = TestExecutionContext.OwnerA;

        await RunAsync(enabled: false);

        Assert.Empty(_seeder.Seeded);
    }

    [Fact]
    public async Task Start_AfterReset_DoesNotSeedAgain()
    {
        _owner.Owner = TestExecutionContext.OwnerA;
        _seeder.SeededBefore = true;

        await RunAsync();

        Assert.Empty(_seeder.Seeded);
    }

    [Fact]
    public async Task OwnerCreatedLater_SeedsWithoutRestart()
    {
        DemoSeedService service = Service();
        await service.StartAsync(Ct);
        await WaitForAsync(() => _owner.Calls > 0);

        _owner.Owner = TestExecutionContext.OwnerA;
        _time.Advance(DemoSeedService.OwnerPoll);
        await WaitForAsync(() => _seeder.Seeded.Count > 0);
        await service.StopAsync(Ct);

        Assert.Equal([TestExecutionContext.OwnerA], _seeder.Seeded);
    }

    private async Task RunAsync(bool enabled = true)
    {
        DemoSeedService service = Service(enabled);
        await service.StartAsync(Ct);
        await service.ExecuteTask!;
    }

    private DemoSeedService Service(bool enabled = true) =>
        new(_seeder, _owner, Options.Create(new DemoOptions { Enabled = enabled }), _time, NullLogger<DemoSeedService>.Instance);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(10, Ct);
        Assert.True(condition());
    }

    private sealed class RecordingSeeder : IDemoSeeder
    {
        public List<OwnerId> Seeded { get; } = [];

        public bool SeededBefore { get; set; }

        public Task<bool> IsSeededAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(Seeded.Contains(owner));

        public Task<bool> WasSeededBeforeAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(SeededBefore || Seeded.Contains(owner));

        public Task<Result<int>> SeedAsync(OwnerId owner, CancellationToken cancellationToken)
        {
            Seeded.Add(owner);
            return Task.FromResult(Result.Success(1));
        }
    }

    private sealed class SettableOwner : IInstanceOwner
    {
        public OwnerId? Owner { get; set; }

        public int Calls { get; private set; }

        public Task<OwnerId> GetAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Owner is { } owner ? Task.FromResult(owner) : throw new InvalidOperationException("The instance owner has not been created yet.");
        }
    }
}
