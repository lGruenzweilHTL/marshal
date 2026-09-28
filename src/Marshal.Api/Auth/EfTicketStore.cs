using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Data;
using Marshal.Data.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Marshal.Api.Auth;

/// <summary>
/// Keeps cookie authentication tickets in the database so IdP tokens never reach the browser and the cookie stays
/// small. Lookups by key run before authentication, so they bypass the owner filter; the key is a 256-bit secret.
/// Tickets are cached in memory for a short time to avoid a database read per request.
/// </summary>
internal sealed class EfTicketStore(
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IDataProtectionProvider protectionProvider,
    IMemoryCache cache,
    TimeProvider timeProvider) : ITicketStore
{
    private static readonly TimeSpan _cacheLifetime = TimeSpan.FromSeconds(30);

    private readonly IDataProtector _protector = protectionProvider.CreateProtector("Marshal.AuthTickets.v1");

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        OwnerId owner = OwnerOf(ticket);
        using IDisposable scope = executionContext.Use(Context(owner));
        await using MarshalDbContext db = await contexts.CreateDbContextAsync();
        DateTimeOffset now = timeProvider.GetUtcNow();
        // Abandoned sessions are never retrieved again; clear every owner's expired tickets on each login.
        await db.AuthTickets.IgnoreQueryFilters().Where(candidate => candidate.ExpiresAt <= now).ExecuteDeleteAsync();
        db.AuthTickets.Add(new AuthTicket { Id = Hash(key), OwnerId = owner, Value = Protect(ticket), CreatedAt = now, ExpiresAt = ticket.Properties.ExpiresUtc });
        await db.SaveChangesAsync();
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        OwnerId owner = OwnerOf(ticket);
        using IDisposable scope = executionContext.Use(Context(owner));
        await using MarshalDbContext db = await contexts.CreateDbContextAsync();
        AuthTicket? row = await db.AuthTickets.SingleOrDefaultAsync(candidate => candidate.Id == Hash(key));
        if (row is null)
            return;

        row.Value = Protect(ticket);
        row.ExpiresAt = ticket.Properties.ExpiresUtc;
        await db.SaveChangesAsync();
        cache.Set(Hash(key), ticket, _cacheLifetime);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        string id = Hash(key);
        if (!cache.TryGetValue(id, out AuthenticationTicket? ticket))
        {
            await using MarshalDbContext db = await contexts.CreateDbContextAsync();
            AuthTicket? row = await db.AuthTickets.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id);
            if (row is null)
                return null;

            ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(row.Value));
            if (ticket is not null)
                cache.Set(id, ticket, _cacheLifetime);
        }

        if (ticket?.Properties.ExpiresUtc is { } expires && expires <= timeProvider.GetUtcNow())
        {
            await RemoveAsync(key);
            return null;
        }

        return ticket;
    }

    public async Task RemoveAsync(string key)
    {
        string id = Hash(key);
        cache.Remove(id);
        await using MarshalDbContext db = await contexts.CreateDbContextAsync();
        AuthTicket? row = await db.AuthTickets.IgnoreQueryFilters().SingleOrDefaultAsync(candidate => candidate.Id == id);
        if (row is null)
            return;

        using IDisposable scope = executionContext.Use(Context(row.OwnerId));
        db.AuthTickets.Remove(row);
        await db.SaveChangesAsync();
    }

    private byte[] Protect(AuthenticationTicket ticket) => _protector.Protect(TicketSerializer.Default.Serialize(ticket));

    private static OwnerId OwnerOf(AuthenticationTicket ticket) =>
        OwnerId.TryParse(ticket.Principal.FindFirstValue(MarshalClaims.Owner), null, out OwnerId owner)
            ? owner
            : throw new InvalidOperationException("An authentication ticket must carry the owner claim.");

    private static MarshalExecutionContext Context(OwnerId owner) => new() { OwnerId = owner, Source = SessionSource.Web, IsInteractive = false };

    private static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
