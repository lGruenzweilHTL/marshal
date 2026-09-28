using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Marshal.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class EfUserProfileProviderTests
{
    [Fact]
    public async Task Get_UserWithPreferences_ReturnsProfile()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using (MarshalDbContext seed = database.CreateContext(OwnerA))
        {
            seed.UserPreferences.Add(new UserPreferences { UserId = OwnerA, Language = "de", TimeZone = "Europe/Vienna" });
            await seed.SaveChangesAsync(Ct);
        }

        await using MarshalDbContext context = database.CreateContext(OwnerA);
        Result<UserProfile> profile = await new EfUserProfileProvider(database.Factory).GetAsync(OwnerA, Ct);

        Assert.Equal(new UserProfile(OwnerA, "Alice", "de", "Europe/Vienna"), profile.Value);
    }

    [Fact]
    public async Task Get_NoPreferences_Defaults()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerB);

        Result<UserProfile> profile = await new EfUserProfileProvider(database.Factory).GetAsync(OwnerB, Ct);

        Assert.Equal(new UserProfile(OwnerB, "Bob", "en", "UTC"), profile.Value);
    }

    [Fact]
    public async Task Get_UnknownUser_NotFound()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);

        Result<UserProfile> profile = await new EfUserProfileProvider(database.Factory).GetAsync(OwnerId.New(), Ct);

        Assert.Equal("user.not_found", profile.Error.Code);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
