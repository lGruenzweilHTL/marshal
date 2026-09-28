using System.Security.Claims;
using System.Text;
using Marshal.Api.Auth;
using Marshal.Data;
using Marshal.Data.Auth;
using Marshal.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Api.Tests.Auth;

public sealed class EfTicketStoreTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
    private TestDatabase _database = null!;
    private EfTicketStore _store = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _store = new EfTicketStore(_database.Factory, _database.Accessor, new EphemeralDataProtectionProvider(), new MemoryCache(new MemoryCacheOptions()), _time);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Store_ThenRetrieve_ReturnsTheTicket()
    {
        string key = await _store.StoreAsync(Ticket("Alice", _time.GetUtcNow().AddHours(1)));

        AuthenticationTicket? ticket = await _store.RetrieveAsync(key);

        Assert.NotNull(ticket);
        Assert.Equal("Alice", ticket.Principal.Identity?.Name);
    }

    [Fact]
    public async Task Store_PersistsProtectedValueUnderTheOwner()
    {
        await _store.StoreAsync(Ticket("Alice", _time.GetUtcNow().AddHours(1), idpToken: "idp-secret"));

        await using (MarshalDbContext ownerA = _database.CreateContext(TestExecutionContext.OwnerA))
        {
            AuthTicket row = await ownerA.AuthTickets.SingleAsync(Ct);
            Assert.Equal(TestExecutionContext.OwnerA, row.OwnerId);
            Assert.DoesNotContain("idp-secret", Encoding.UTF8.GetString(row.Value), StringComparison.Ordinal);
        }

        await using MarshalDbContext ownerB = _database.CreateContext(TestExecutionContext.OwnerB);
        Assert.Empty(await ownerB.AuthTickets.ToListAsync(Ct));
    }

    [Fact]
    public async Task Renew_ReplacesTicketAndExpiry()
    {
        string key = await _store.StoreAsync(Ticket("Alice", _time.GetUtcNow().AddHours(1)));
        _time.Advance(TimeSpan.FromMinutes(90));

        await _store.RenewAsync(key, Ticket("Alice renewed", _time.GetUtcNow().AddHours(1)));

        AuthenticationTicket? ticket = await _store.RetrieveAsync(key);
        Assert.Equal("Alice renewed", ticket?.Principal.Identity?.Name);
    }

    [Fact]
    public async Task Retrieve_Expired_ReturnsNullAndDeletesTheRow()
    {
        string key = await _store.StoreAsync(Ticket("Alice", _time.GetUtcNow().AddHours(1)));
        _time.Advance(TimeSpan.FromHours(2));

        Assert.Null(await _store.RetrieveAsync(key));

        await using MarshalDbContext db = _database.CreateContext(TestExecutionContext.OwnerA);
        Assert.Empty(await db.AuthTickets.ToListAsync(Ct));
    }

    [Fact]
    public async Task Store_PurgesExpiredTicketsNobodyRetrieves()
    {
        await _store.StoreAsync(Ticket("Alice", _time.GetUtcNow().AddHours(1)));
        _time.Advance(TimeSpan.FromHours(2));

        await _store.StoreAsync(Ticket("Alice again", _time.GetUtcNow().AddHours(1)));

        await using MarshalDbContext db = _database.CreateContext(TestExecutionContext.OwnerA);
        Assert.Single(await db.AuthTickets.ToListAsync(Ct));
    }

    [Fact]
    public async Task Remove_ThenRetrieve_ReturnsNull()
    {
        string key = await _store.StoreAsync(Ticket("Alice", _time.GetUtcNow().AddHours(1)));
        await _store.RetrieveAsync(key);

        await _store.RemoveAsync(key);

        Assert.Null(await _store.RetrieveAsync(key));
    }

    [Fact]
    public async Task Retrieve_UnknownKey_ReturnsNull() =>
        Assert.Null(await _store.RetrieveAsync("unknown"));

    [Fact]
    public async Task Store_TicketWithoutOwner_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "nobody")], "Cookies"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.StoreAsync(new AuthenticationTicket(principal, "Cookies")));
    }

    private static AuthenticationTicket Ticket(string name, DateTimeOffset expires, string? idpToken = null)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name), new Claim(MarshalClaims.Owner, TestExecutionContext.OwnerA.ToString())], "Cookies"));
        var properties = new AuthenticationProperties { ExpiresUtc = expires };
        if (idpToken is not null)
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = idpToken }]);

        return new AuthenticationTicket(principal, properties, "Cookies");
    }
}
