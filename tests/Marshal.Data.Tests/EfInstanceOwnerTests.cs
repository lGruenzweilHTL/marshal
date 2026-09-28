using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Marshal.Testing;

namespace Marshal.Data.Tests;

public sealed class EfInstanceOwnerTests
{
    [Fact]
    public async Task Get_ReturnsFirstCreatedUser()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext();
        context.Users.Add(new User { Id = OwnerId.New(), UserName = "late", DisplayName = "Late", SecurityStamp = "s", CreatedAt = DateTimeOffset.UnixEpoch.AddDays(-1) });
        await context.SaveChangesAsync(Ct);

        OwnerId owner = await new EfInstanceOwner(context).GetAsync(Ct);

        Assert.Equal("Late", context.Users.Single(user => user.Id == owner).DisplayName);
    }

    [Fact]
    public async Task ListUsers_ReturnsAll()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext();

        IReadOnlyList<OwnerId> owners = await new EfUserDirectory(context).ListAsync(Ct);

        Assert.Equal(new HashSet<OwnerId> { TestExecutionContext.OwnerA, TestExecutionContext.OwnerB }, owners.ToHashSet());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
