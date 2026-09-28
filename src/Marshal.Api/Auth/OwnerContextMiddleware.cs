using System.Security.Claims;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;

namespace Marshal.Api.Auth;

/// <summary>
/// Makes the authenticated principal's owner the execution context of the request, so owner-scoped queries in
/// endpoints see only that owner's rows. Runs keep setting their own, richer context.
/// </summary>
internal sealed class OwnerContextMiddleware(RequestDelegate next, IExecutionContextAccessor executionContext)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!OwnerId.TryParse(context.User.FindFirstValue(MarshalClaims.Owner), null, out OwnerId owner))
        {
            await next(context);
            return;
        }

        SessionSource source = context.User.HasClaim(claim => claim.Type == MarshalClaims.TokenId) ? SessionSource.Api : SessionSource.Web;
        using (executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = source, IsInteractive = false }))
            await next(context);
    }
}
