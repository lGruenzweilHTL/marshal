using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Api.Problems;
using Marshal.Data.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Marshal.Api.Auth;

/// <summary>
/// The <c>local</c> provider: first-run setup (claims the instance owner), password login, passkey login and passkey
/// registration for the signed-in user. Every endpoint is 404 unless a <c>local</c> provider is configured.
/// </summary>
internal static class LocalAuthEndpoints
{
    public static RouteGroupBuilder MapLocalAuthEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder local = group.MapGroup("/auth/local").WithTags("Auth").AddEndpointFilter(RequireLocalProvider);
        local.MapGet("/setup", GetSetupAsync).AllowAnonymous().WithName("GetLocalSetup")
            .WithSummary("200 while the instance owner is unclaimed and the first-run setup is available, otherwise 404.");
        local.MapPost("/setup", SetupAsync).AllowAnonymous().WithName("CompleteLocalSetup")
            .WithSummary("Claims the instance owner: sets user name, display name and password, grants admin and signs in.");
        local.MapPost("/login", PasswordLoginAsync).AllowAnonymous().WithName("LocalPasswordLogin")
            .WithSummary("Signs in with user name and password.");
        local.MapPost("/passkey/options", PasskeyRequestOptionsAsync).AllowAnonymous().WithName("LocalPasskeyRequestOptions")
            .WithSummary("WebAuthn request options (PublicKeyCredentialRequestOptions JSON) for passkey login.");
        local.MapPost("/passkey", PasskeyLoginAsync).AllowAnonymous().WithName("LocalPasskeyLogin")
            .WithSummary("Signs in with a passkey assertion (PublicKeyCredential JSON).");
        local.MapPost("/passkeys/creation-options", PasskeyCreationOptionsAsync).RequireAuthorization().WithName("LocalPasskeyCreationOptions")
            .WithSummary("WebAuthn creation options (PublicKeyCredentialCreationOptions JSON) to add a passkey to the signed-in user.");
        local.MapPost("/passkeys", RegisterPasskeyAsync).RequireAuthorization().WithName("RegisterLocalPasskey")
            .WithSummary("Stores a passkey attestation for the signed-in user.");
        return group;
    }

    private static async ValueTask<object?> RequireLocalProvider(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        LocalProvider(context.HttpContext) is null
            ? new Error("auth.local_not_configured", "The local login provider is not configured.", ErrorType.NotFound).ToProblem()
            : await next(context);

    private static async Task<Results<Ok<LocalSetupStatus>, ProblemHttpResult>> GetSetupAsync(UserAccounts accounts, CancellationToken ct) =>
        await accounts.FindUnclaimedOwnerAsync(ct) is null ? SetupUnavailable() : TypedResults.Ok(new LocalSetupStatus(true));

    private static async Task<Results<NoContent, ProblemHttpResult>> SetupAsync(LocalSetupRequest request, UserAccounts accounts, HttpContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrEmpty(request.Password))
            return new Error("auth.setup_invalid", "userName and password are required.", ErrorType.Validation).ToProblem();

        await UserAccounts.ProvisioningLock.WaitAsync(ct);
        try
        {
            User? owner = await accounts.FindUnclaimedOwnerAsync(ct);
            if (owner is null)
                return SetupUnavailable();

            owner.UserName = request.UserName.Trim();
            owner.DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? owner.UserName : request.DisplayName.Trim();
            owner.LockoutEnabled = true;
            // Validates password and user name together and saves only when both pass.
            IdentityResult result = await accounts.Users.AddPasswordAsync(owner, request.Password);
            if (!result.Succeeded)
                return IdentityProblem("auth.setup_invalid", result);

            string[] roles = [Roles.Admin];
            await accounts.SyncRolesAsync(owner, roles);
            await SignInAsync(context, owner, roles);
            return TypedResults.NoContent();
        }
        finally
        {
            UserAccounts.ProvisioningLock.Release();
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PasswordLoginAsync(LocalLoginRequest request, UserAccounts accounts, SignInManager<User> signIn, HttpContext context)
    {
        User? user = string.IsNullOrEmpty(request.UserName) ? null : await accounts.Users.FindByNameAsync(request.UserName);
        if (user is null || string.IsNullOrEmpty(request.Password))
            return InvalidCredentials();

        Microsoft.AspNetCore.Identity.SignInResult result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return InvalidCredentials();

        await SignInAsync(context, user, await accounts.Users.GetRolesAsync(user));
        return TypedResults.NoContent();
    }

    private static async Task<ContentHttpResult> PasskeyRequestOptionsAsync(PasskeyOptionsRequest? request, UserAccounts accounts, SignInManager<User> signIn)
    {
        User? user = string.IsNullOrEmpty(request?.UserName) ? null : await accounts.Users.FindByNameAsync(request.UserName);
        return TypedResults.Content(await signIn.MakePasskeyRequestOptionsAsync(user), "application/json");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PasskeyLoginAsync(PasskeyCredentialRequest request, UserAccounts accounts, SignInManager<User> signIn, HttpContext context)
    {
        PasskeyAssertionResult<User> assertion;
        try
        {
            assertion = await signIn.PerformPasskeyAssertionAsync(request.CredentialJson);
        }
        catch (InvalidOperationException)
        {
            return new Error("auth.passkey_not_started", "Request passkey options first.", ErrorType.Validation).ToProblem();
        }

        if (!assertion.Succeeded || !await signIn.CanSignInAsync(assertion.User) || await accounts.Users.IsLockedOutAsync(assertion.User))
            return InvalidCredentials();

        UserAccounts.Check(await accounts.Users.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey));
        await SignInAsync(context, assertion.User, await accounts.Users.GetRolesAsync(assertion.User));
        return TypedResults.NoContent();
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> PasskeyCreationOptionsAsync(ClaimsPrincipal principal, UserAccounts accounts, SignInManager<User> signIn)
    {
        User? user = await CurrentUserAsync(principal, accounts);
        if (user is null)
            return new Error("auth.user_not_found", "The signed-in user no longer exists.", ErrorType.NotFound).ToProblem();

        string options = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.UserName ?? user.Id.ToString(),
            DisplayName = user.DisplayName,
        });
        return TypedResults.Content(options, "application/json");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RegisterPasskeyAsync(PasskeyRegistrationRequest request, ClaimsPrincipal principal, UserAccounts accounts, SignInManager<User> signIn)
    {
        User? user = await CurrentUserAsync(principal, accounts);
        if (user is null)
            return new Error("auth.user_not_found", "The signed-in user no longer exists.", ErrorType.NotFound).ToProblem();

        PasskeyAttestationResult attestation;
        try
        {
            attestation = await signIn.PerformPasskeyAttestationAsync(request.CredentialJson);
        }
        catch (InvalidOperationException)
        {
            return new Error("auth.passkey_not_started", "Request creation options first.", ErrorType.Validation).ToProblem();
        }

        // The attestation state names the account; it must be the signed-in user.
        if (!attestation.Succeeded || attestation.UserEntity.Id != user.Id.ToString())
            return new Error("auth.passkey_invalid", "The passkey could not be verified.", ErrorType.Validation).ToProblem();

        attestation.Passkey.Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        IdentityResult stored = await accounts.Users.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        return stored.Succeeded ? TypedResults.NoContent() : IdentityProblem("auth.passkey_invalid", stored);
    }

    private static ILoginProvider? LocalProvider(HttpContext context) =>
        context.RequestServices.GetRequiredService<IReadOnlyList<ILoginProvider>>().FirstOrDefault(provider => provider.Type == LoginProviderOptions.LocalType);

    private static Task SignInAsync(HttpContext context, User user, IEnumerable<string> roles) =>
        context.SignInAsync(MarshalAuthSchemes.Cookie, UserAccounts.CreatePrincipal(user, roles, LocalProvider(context)!.Name));

    private static async Task<User?> CurrentUserAsync(ClaimsPrincipal principal, UserAccounts accounts) =>
        principal.FindFirstValue(MarshalClaims.Owner) is { } owner ? await accounts.Users.FindByIdAsync(owner) : null;

    private static ProblemHttpResult SetupUnavailable() =>
        new Error("auth.setup_unavailable", "The first-run setup is not available.", ErrorType.NotFound).ToProblem();

    private static ProblemHttpResult InvalidCredentials() =>
        TypedResults.Problem(title: "auth.invalid_credentials", detail: "The credentials are not valid.", statusCode: StatusCodes.Status401Unauthorized,
            extensions: new Dictionary<string, object?> { ["code"] = "auth.invalid_credentials" });

    private static ProblemHttpResult IdentityProblem(string code, IdentityResult result) =>
        new Error(code, string.Join(" ", result.Errors.Select(error => error.Description)), ErrorType.Validation).ToProblem();
}

/// <summary>Whether the first-run setup is available.</summary>
public sealed record LocalSetupStatus(bool Available);

/// <summary>First-run setup: the instance owner's login.</summary>
public sealed record LocalSetupRequest(string UserName, string? DisplayName, string Password);

/// <summary>Password login.</summary>
public sealed record LocalLoginRequest(string UserName, string Password);

/// <summary>Passkey login options; without a user name the options allow any discoverable passkey.</summary>
public sealed record PasskeyOptionsRequest(string? UserName);

/// <summary>A <c>PublicKeyCredential</c> serialized with <c>toJSON()</c>.</summary>
public sealed record PasskeyCredentialRequest(string CredentialJson);

/// <summary>A new passkey for the signed-in user.</summary>
public sealed record PasskeyRegistrationRequest(string CredentialJson, string? Name);
