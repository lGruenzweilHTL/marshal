using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Marshal.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Tests.Identity;

public sealed class IdentityUserStoreTests
{
    [Fact]
    public void User_IsIdentityUserKeyedByOwnerId()
    {
        var id = OwnerId.New();

        IdentityUser<OwnerId> user = new User { Id = id, UserName = "alice", DisplayName = "Alice", CreatedAt = DateTimeOffset.UnixEpoch };

        Assert.Equal(id, user.Id);
    }

    [Fact]
    public void OwnerId_RoundTripsThroughTheStringFormIdentityUses()
    {
        var id = OwnerId.New();

        object? parsed = System.ComponentModel.TypeDescriptor.GetConverter(typeof(OwnerId)).ConvertFromInvariantString(id.ToString());

        Assert.Equal(id, parsed);
    }

    [Fact]
    public async Task IdentityTables_StoreLoginsRolesAndPasskeysForAUser()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        OwnerId userId = TestExecutionContext.OwnerA;
        await using (MarshalDbContext db = database.CreateContext())
        {
            var role = new Role { Id = OwnerId.New(), Name = "admin", NormalizedName = "ADMIN" };
            db.Roles.Add(role);
            db.UserRoles.Add(new IdentityUserRole<OwnerId> { UserId = userId, RoleId = role.Id });
            db.UserLogins.Add(new IdentityUserLogin<OwnerId> { LoginProvider = "authentik", ProviderKey = "sub-1", UserId = userId });
            db.UserPasskeys.Add(new IdentityUserPasskey<OwnerId>
            {
                UserId = userId,
                CredentialId = [1, 2, 3],
                Data = new IdentityPasskeyData { PublicKey = [4], Name = "phone", CreatedAt = DateTimeOffset.UnixEpoch, Transports = ["internal"], AttestationObject = [5], ClientDataJson = [6] },
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using MarshalDbContext read = database.CreateContext();
        Assert.Equal(userId, (await read.UserLogins.SingleAsync(TestContext.Current.CancellationToken)).UserId);
        Assert.Equal(userId, (await read.UserRoles.SingleAsync(TestContext.Current.CancellationToken)).UserId);
        Assert.Equal("phone", (await read.UserPasskeys.SingleAsync(TestContext.Current.CancellationToken)).Data.Name);
        Assert.Equal("ADMIN", (await read.Roles.SingleAsync(TestContext.Current.CancellationToken)).NormalizedName);
    }
}
