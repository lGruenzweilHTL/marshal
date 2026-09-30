using Marshal.Data;
using Marshal.Data.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Auth;

/// <summary>
/// Authentication: machine tokens, the BFF session cookie (tickets server-side) and one OIDC handler per configured
/// <c>oidc</c> provider.
/// </summary>
internal static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>().Bind(configuration.GetSection("auth")).ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddMemoryCache();
        services.AddSingleton<IClaimsTransformation, ScopeClaimsTransformation>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "marshal_af";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddIdentityCore<User>(options =>
            {
                // Length over composition rules (NIST SP 800-63B); lockout after repeated failures.
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<Role>()
            .AddEntityFrameworkStores<MarshalDbContext>()
            .AddSignInManager();
        services.AddOptions<IdentityPasskeyOptions>().Configure<IOptions<AuthOptions>>((options, auth) =>
            options.ServerDomain = auth.Value.Providers.FirstOrDefault(provider => provider.Type == LoginProviderOptions.LocalType)?.ServerDomain);
        services.AddScoped<UserAccounts>();
        services.AddScoped<OidcLoginMapper>();
        services.AddSingleton<IRoleProvisioner, GroupClaimRoleProvisioner>();
        services.AddSingleton<ITicketStore, EfTicketStore>();

        AuthenticationBuilder authentication = services.AddAuthentication(MarshalAuthSchemes.Default)
            .AddPolicyScheme(MarshalAuthSchemes.Default, null, options => options.ForwardDefaultSelector = SelectScheme)
            .AddScheme<AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(MarshalAuthSchemes.ApiToken, null)
            .AddScheme<AuthenticationSchemeOptions, ExecutorCallbackAuthenticationHandler>(MarshalAuthSchemes.ExecutorCallback, null)
            .AddCookie(MarshalAuthSchemes.Cookie, ConfigureCookie)
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme, ConfigurePasskeyCeremonyCookie);
        services.AddOptions<CookieAuthenticationOptions>(MarshalAuthSchemes.Cookie)
            .Configure<ITicketStore>((options, store) => options.SessionStore = store);

        // One OIDC scheme per configured provider. Registered through AuthenticationOptions (not AddOpenIdConnect) so the
        // provider list is read when the options are first used, not while services are registered.
        services.AddOptions<AuthenticationOptions>().Configure<IOptions<AuthOptions>>((options, auth) =>
        {
            foreach (LoginProviderOptions provider in OidcProviders(auth.Value))
                options.AddScheme(provider.Name, scheme => { scheme.HandlerType = typeof(OpenIdConnectHandler); scheme.DisplayName = provider.EffectiveDisplayName; });
        });
        services.AddTransient<OpenIdConnectHandler>();
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, OidcOptionsSetup>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<OpenIdConnectOptions>, OpenIdConnectPostConfigureOptions>());
        services.AddSingleton<IReadOnlyList<ILoginProvider>>(provider => [.. CreateLoginProviders(provider.GetRequiredService<IOptions<AuthOptions>>().Value)]);

        return services;
    }

    private static string SelectScheme(HttpContext context) =>
        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? MarshalAuthSchemes.ApiToken
            : MarshalAuthSchemes.Cookie;

    private static void ConfigureCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "marshal_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        // Lax, so deep links from push notifications (top-level navigations) arrive logged in.
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // The SPA handles navigation; API calls get status codes instead of redirects.
        options.Events.OnRedirectToLogin = context => StatusOnly(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => StatusOnly(context, StatusCodes.Status403Forbidden);
    }

    /// <summary>Identity keeps the passkey ceremony state (challenge) in this short-lived, protected cookie.</summary>
    private static void ConfigurePasskeyCeremonyCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "marshal_passkey";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    }

    private static Task StatusOnly(RedirectContext<CookieAuthenticationOptions> context, int status)
    {
        context.Response.StatusCode = status;
        return Task.CompletedTask;
    }

    private static IEnumerable<LoginProviderOptions> OidcProviders(AuthOptions auth) =>
        auth.Providers.Where(provider => provider.Type == LoginProviderOptions.OidcType).DistinctBy(provider => provider.Name);

    private static IEnumerable<ILoginProvider> CreateLoginProviders(AuthOptions auth)
    {
        foreach (LoginProviderOptions provider in auth.Providers.DistinctBy(provider => provider.Name))
        {
            if (provider.Type == LoginProviderOptions.OidcType)
                yield return new OidcLoginProvider(provider.Name, provider.EffectiveDisplayName);
            else if (provider.Type == LoginProviderOptions.LocalType)
                yield return new LocalLoginProvider(provider.Name, provider.EffectiveDisplayName);
        }
    }

    /// <summary>Applies <c>auth.providers[i]</c> to the OIDC handler options named after the provider.</summary>
    private sealed class OidcOptionsSetup(IOptions<AuthOptions> auth) : IConfigureNamedOptions<OpenIdConnectOptions>
    {
        public void Configure(string? name, OpenIdConnectOptions options)
        {
            LoginProviderOptions? provider = OidcProviders(auth.Value).FirstOrDefault(candidate => candidate.Name == name);
            if (provider is not null)
                ConfigureOidc(options, provider);
        }

        public void Configure(OpenIdConnectOptions options) => Configure(Options.DefaultName, options);
    }

    private static void ConfigureOidc(OpenIdConnectOptions options, LoginProviderOptions provider)
    {
        options.Authority = provider.Authority?.ToString();
        options.ClientId = provider.ClientId;
        options.ClientSecret = provider.ClientSecret;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = true;
        options.MapInboundClaims = false;
        options.GetClaimsFromUserInfoEndpoint = provider.UseUserInfo;
        options.SignInScheme = MarshalAuthSchemes.Cookie;
        options.CallbackPath = $"/signin-oidc/{provider.Name}";
        options.SignedOutCallbackPath = $"/signout-callback-oidc/{provider.Name}";
        options.TokenValidationParameters.NameClaimType = provider.Claims.Name;
        options.Events.OnTicketReceived = context => context.HttpContext.RequestServices.GetRequiredService<OidcLoginMapper>().MapAsync(context, provider);
        options.Events.OnRemoteFailure = context =>
        {
            context.Response.Redirect($"{context.Request.PathBase}/login?error=login_failed");
            context.HandleResponse();
            return Task.CompletedTask;
        };
        options.Scope.Clear();
        foreach (string scope in provider.Scopes)
            options.Scope.Add(scope);
    }
}
