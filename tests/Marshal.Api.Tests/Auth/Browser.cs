using System.Net.Http.Json;
using Marshal.Abstractions.Identity;
using Marshal.Api.Tests.Http;
using Marshal.Data.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Auth;

/// <summary>
/// An HTTP client that behaves like the SPA in a browser: https origin, cookies, no auto-redirects and (unless turned
/// off) an antiforgery header on every state-changing request.
/// </summary>
internal static class Browser
{
    public const string Origin = "https://localhost";

    public const string Password = "correct horse battery staple";

    /// <summary>Request header that makes the browser skip the antiforgery token for that one request.</summary>
    public const string SkipAntiforgery = "X-Test-Skip-Antiforgery";

    public static HttpClient Create(MarshalApiFactory factory, bool antiforgery = true)
    {
        DelegatingHandler[] handlers = antiforgery ? [new AntiforgeryHandler(), new CookieContainerHandler()] : [new CookieContainerHandler()];
        HttpClient client = factory.CreateDefaultClient(new Uri(Origin), handlers);
        // Browsers send Origin on fetch POSTs; Identity checks WebAuthn client data against it.
        client.DefaultRequestHeaders.Add("Origin", Origin);
        return client;
    }

    /// <summary>Creates a local user with <paramref name="roles"/> and returns a browser signed in as that user.</summary>
    public static async Task<(HttpClient Client, OwnerId User)> SignInAsync(MarshalApiFactory factory, params string[] roles)
    {
        _ = factory.Server;
        string userName = "user-" + Guid.NewGuid().ToString("N")[..8];
        OwnerId id;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            UserManager<User> users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            RoleManager<Role> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
            var user = new User { Id = OwnerId.New(), UserName = userName, DisplayName = userName, CreatedAt = DateTimeOffset.UtcNow };
            Check(await users.CreateAsync(user, Password));
            foreach (string role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    Check(await roleManager.CreateAsync(new Role { Id = OwnerId.New(), Name = role }));
            }

            Check(await users.AddToRolesAsync(user, roles));
            id = user.Id;
        }

        HttpClient client = Create(factory);
        HttpResponseMessage login = await client.PostAsJsonAsync("/api/v1/auth/local/login", new { userName, password = Password }, TestContext.Current.CancellationToken);
        login.EnsureSuccessStatusCode();
        return (client, id);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    /// <summary>Fetches a fresh token before each state-changing request, as the SPA does after login changes.</summary>
    private sealed class AntiforgeryHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Remove(SkipAntiforgery))
                return await base.SendAsync(request, cancellationToken);

            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            {
                using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(request.RequestUri!, "/api/v1/auth/antiforgery"));
                tokenRequest.Headers.Add("Origin", Origin);
                HttpResponseMessage tokenResponse = await base.SendAsync(tokenRequest, cancellationToken);
                tokenResponse.EnsureSuccessStatusCode();
                AntiforgeryBody? body = await tokenResponse.Content.ReadFromJsonAsync<AntiforgeryBody>(cancellationToken);
                request.Headers.Add("X-XSRF-TOKEN", body!.Token);
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed record AntiforgeryBody(string Token);
}
