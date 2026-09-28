using System.Net.Http.Headers;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Abstractions.Tools;
using Marshal.Core.Models;
using Marshal.Data;
using Marshal.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.Api.Tests.Http;

/// <summary>
/// The real host in-process, with temp volumes and a scripted fake model for every configured model.
/// </summary>
public sealed class MarshalApiFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    public MarshalApiFactory(Dictionary<string, string?>? settings = null, params ITool[] tools)
    {
        Root = Directory.CreateTempSubdirectory("marshal-api-").FullName;
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(SecretsDirectory);
        _settings = new Dictionary<string, string?>
        {
            ["paths:config"] = ConfigDirectory,
            ["paths:data"] = Path.Combine(Root, "data"),
            ["paths:secrets"] = SecretsDirectory,
            ["models:fast:tier"] = "fast",
            ["models:fast:provider"] = "ollama",
            ["models:fast:model"] = "qwen3",
            ["models:standard:tier"] = "standard",
            ["models:standard:provider"] = "ollama",
            ["models:standard:model"] = "qwen3",
            ["owner:displayName"] = "Lukas",
            ["owner:language"] = "en",
            ["instance:timeZone"] = "Europe/Vienna",
        };
        foreach ((string key, string? value) in settings ?? [])
            _settings[key] = value;

        Tools = tools;
    }

    public string Root { get; }

    public string ConfigDirectory => Path.Combine(Root, "config");

    public string SecretsDirectory => Path.Combine(Root, "secrets");

    public FakeChatClient Model { get; } = new();

    public IReadOnlyList<ITool> Tools { get; }

    /// <summary>Extra service registrations applied after the host's own (e.g. a fake IdP backchannel).</summary>
    public Action<IServiceCollection>? TestServices { get; set; }

    /// <summary>Creates a token for the instance owner and returns a client that sends it.</summary>
    public async Task<HttpClient> ClientWithTokenAsync(params string[] scopes)
    {
        string token = await CreateTokenAsync(scopes);
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public async Task<string> CreateTokenAsync(params string[] scopes)
    {
        using IServiceScope scope = Services.CreateScope();
        OwnerId owner = await scope.ServiceProvider.GetRequiredService<IInstanceOwner>().GetAsync(CancellationToken.None);
        using (scope.ServiceProvider.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Api }))
            return (await scope.ServiceProvider.GetRequiredService<IApiTokenService>().CreateAsync("test", scopes, null, CancellationToken.None)).Value.Plaintext;
    }

    /// <summary>Runs <paramref name="work"/> on a database context scoped to <paramref name="owner"/>.</summary>
    public async Task<T> WithDatabaseAsync<T>(OwnerId owner, Func<MarshalDbContext, Task<T>> work)
    {
        _ = Server;
        using IDisposable scope = Services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web });
        await using MarshalDbContext db = await Services.GetRequiredService<IDbContextFactory<MarshalDbContext>>().CreateDbContextAsync();
        return await work(db);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(_settings));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IChatClientFactory>();
            services.AddSingleton<IChatClientFactory>(new FakeFactory(Model));
            foreach (ITool tool in Tools)
                services.AddSingleton(tool);

            TestServices?.Invoke(services);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (disposing && Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    private sealed class FakeFactory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create(string key, ModelOptions options) => client;
    }
}
