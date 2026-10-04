using Marshal.Abstractions;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Antiforgery;

namespace Marshal.Api.Auth;

/// <summary>
/// Requires the antiforgery header (<c>X-XSRF-TOKEN</c>) on every state-changing API request that is not
/// bearer-authenticated. Browsers cannot attach bearer tokens cross-site, so only cookie and anonymous requests (login,
/// setup) need it. Runs after authorization, so unauthenticated calls to protected endpoints still get 401. The SPA
/// gets a token from <c>GET /api/v1/auth/antiforgery</c>.
/// </summary>
internal sealed class AntiforgeryValidationMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (NeedsToken(context.Request))
        {
            // The session and antiforgery cookies are Secure-only; behind the TLS proxy requests arrive as https.
            if (!context.Request.IsHttps)
            {
                await AntiforgeryErrors.HttpsRequired.ToProblem().ExecuteAsync(context);
                return;
            }

            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await new Error("auth.antiforgery_invalid", "The antiforgery token is missing or invalid.", ErrorType.Validation).ToProblem().ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }

    private static bool NeedsToken(HttpRequest request) =>
        !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method) && !HttpMethods.IsTrace(request.Method)
        && request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
        && !request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Failures of browser sessions shared by the antiforgery token endpoint and the validation middleware.</summary>
internal static class AntiforgeryErrors
{
    public static readonly Error HttpsRequired = new("auth.https_required", "Browser sessions require HTTPS.", ErrorType.Validation);
}
