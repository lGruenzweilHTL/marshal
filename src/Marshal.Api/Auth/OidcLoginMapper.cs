using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Marshal.Api.Auth;

/// <summary>
/// Turns a completed OIDC login into a Marshal session: roles from groups (deny when unmapped), user matched by
/// subject (never email), the unclaimed instance owner claimed by the first subject, roles re-synced every login.
/// </summary>
internal sealed partial class OidcLoginMapper(UserAccounts accounts, IRoleProvisioner provisioner, TimeProvider timeProvider, ILogger<OidcLoginMapper> logger)
{
    public async Task MapAsync(TicketReceivedContext context, LoginProviderOptions provider)
    {
        ClaimsPrincipal external = context.Principal ?? throw new InvalidOperationException("The OIDC handler produced no principal.");
        string? subject = external.FindFirst(provider.Claims.Subject)?.Value;
        Result<IReadOnlyList<string>> roles = provisioner.RolesFor(provider, external);
        if (subject is null || roles.IsFailure)
        {
            LogDenied(logger, provider.Name, subject);
            context.Response.Redirect($"{context.Request.PathBase}/login?error=access_denied");
            context.HandleResponse();
            return;
        }

        User user = await FindOrProvisionAsync(provider, subject, external, context.HttpContext.RequestAborted);
        await accounts.SyncRolesAsync(user, roles.Value);
        context.Principal = UserAccounts.CreatePrincipal(user, roles.Value, provider.Name);
    }

    private async Task<User> FindOrProvisionAsync(LoginProviderOptions provider, string subject, ClaimsPrincipal external, CancellationToken ct)
    {
        UserManager<User> users = accounts.Users;
        if (await users.FindByLoginAsync(provider.Name, subject) is { } existing)
            return existing;

        await UserAccounts.ProvisioningLock.WaitAsync(ct);
        try
        {
            if (await users.FindByLoginAsync(provider.Name, subject) is { } raced)
                return raced;

            User user = await accounts.FindUnclaimedOwnerAsync(ct) ?? await CreateAsync(external, provider, subject);
            UserAccounts.Check(await users.AddLoginAsync(user, new UserLoginInfo(provider.Name, subject, provider.EffectiveDisplayName)));
            return user;
        }
        finally
        {
            UserAccounts.ProvisioningLock.Release();
        }
    }

    private async Task<User> CreateAsync(ClaimsPrincipal external, LoginProviderOptions provider, string subject)
    {
        var id = OwnerId.New();
        var user = new User
        {
            Id = id,
            UserName = id.ToString(),
            DisplayName = external.FindFirst(provider.Claims.Name)?.Value ?? subject,
            Email = external.FindFirst("email")?.Value,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        UserAccounts.Check(await accounts.Users.CreateAsync(user));
        return user;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "OIDC login via {Provider} denied for subject {Subject}: no mapped group")]
    private static partial void LogDenied(ILogger logger, string provider, string? subject);
}
