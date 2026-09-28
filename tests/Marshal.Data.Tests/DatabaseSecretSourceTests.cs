using System.Text;
using Marshal.Abstractions.Secrets;
using Marshal.Data.Secrets;
using Marshal.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class DatabaseSecretSourceTests
{
    private static readonly SecretName _freshRss = SecretName.Create("freshrss").Value;

    private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task Set_StoresCiphertextNotPlaintext()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);

        await Source(database).SetAsync(_freshRss, "plain-password", OwnerA, TestContext.Current.CancellationToken);

        Marshal.Data.Secrets.Secret row = await context.Secrets.SingleAsync(TestContext.Current.CancellationToken);
        Assert.DoesNotContain("plain-password", Encoding.UTF8.GetString(row.Ciphertext), StringComparison.Ordinal);
        Assert.Equal(OwnerA, row.OwnerId);
    }

    [Fact]
    public async Task TryGet_OwnerRow_OnlyForThatOwner()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using (MarshalDbContext context = database.CreateContext(OwnerA))
            await Source(database).SetAsync(_freshRss, "a-value", OwnerA, TestContext.Current.CancellationToken);

        await using MarshalDbContext asB = database.CreateContext(OwnerB);
        string? forB = await Source(database).TryGetAsync(_freshRss, OwnerB, TestContext.Current.CancellationToken);
        string? instance = await Source(database).TryGetAsync(_freshRss, null, TestContext.Current.CancellationToken);

        Assert.Null(forB);
        Assert.Null(instance);
    }

    [Fact]
    public async Task TryGet_InstanceRow_ReadableWithoutExecutionContext()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using (MarshalDbContext context = database.CreateContext(OwnerA))
        {
            await Source(database).SetAsync(_freshRss, "instance-value", null, TestContext.Current.CancellationToken);
            await Source(database).SetAsync(SecretName.Create("owner_only").Value, "a-value", OwnerA, TestContext.Current.CancellationToken);
        }

        database.Accessor.Current = null;
        await using MarshalDbContext noContext = database.CreateContext();
        string? value = await Source(database).TryGetAsync(_freshRss, null, TestContext.Current.CancellationToken);
        int visible = await noContext.Secrets.CountAsync(TestContext.Current.CancellationToken);

        Assert.Equal("instance-value", value);
        Assert.Equal(1, visible);
    }

    [Fact]
    public async Task Set_Existing_ReplacesValue()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DatabaseSecretSource source = Source(database);

        await source.SetAsync(_freshRss, "first", OwnerA, TestContext.Current.CancellationToken);
        await source.SetAsync(_freshRss, "second", OwnerA, TestContext.Current.CancellationToken);

        Assert.Equal("second", await source.TryGetAsync(_freshRss, OwnerA, TestContext.Current.CancellationToken));
        Assert.Equal(1, await context.Secrets.CountAsync(TestContext.Current.CancellationToken));
    }

    private DatabaseSecretSource Source(TestDatabase database) => new(database.Factory, _protection, _time);
}
