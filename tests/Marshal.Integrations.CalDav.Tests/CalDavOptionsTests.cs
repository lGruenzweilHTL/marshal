using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Integrations.CalDav.Sync;
using Microsoft.Extensions.Hosting;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Health;
using Marshal.Abstractions.Secrets;
using Marshal.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.CalDav.Tests;

public sealed class CalDavOptionsTests
{
    private const string Account = "calendar:caldav:accounts:nextcloud";

    [Fact]
    public void Account_BindsWithDefaults()
    {
        CalDavOptions options = Options(Valid());

        CalDavAccountOptions account = Assert.Single(options.Accounts).Value;
        Assert.Equal(new Uri("https://cloud.example.org/remote.php/dav"), account.Url);
        Assert.Equal(("lukas", "nextcloud_caldav", 5), (account.Username, account.PasswordSecret, account.PollMinutes));
        Assert.Null(account.Calendars);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Calendars_AndPollMinutes()
    {
        CalDavOptions options = Options([.. Valid(), ($"{Account}:calendars:0", "personal"), ($"{Account}:pollMinutes", "15")]);

        CalDavAccountOptions account = options.Accounts["nextcloud"];
        Assert.Equal(["personal"], account.Calendars);
        Assert.Equal(15, account.PollMinutes);
    }

    [Fact]
    public void Registration_ProviderIsCalendarSourceAndHealthProvider()
    {
        ServiceProvider services = Services(Valid());

        CalDavCalendarProvider provider = services.GetRequiredService<CalDavCalendarProvider>();
        Assert.Same(provider, Assert.Single(services.GetServices<ICalendarSource>()));
        Assert.Same(provider, Assert.Single(services.GetServices<IHealthWarningProvider>()));
        Assert.True(provider.IsConfigured);
    }

    [Fact]
    public void Registration_ChangePollerOnlyWithAccounts()
    {
        Assert.Contains(Services(Valid()).GetServices<IHostedService>(), service => service is CalDavSyncPoller);
        Assert.Empty(Services([]).GetServices<IHostedService>());
    }

    [Fact]
    public void NoSection_NotConfigured() => Assert.False(Options([]).IsConfigured);

    [Theory]
    [InlineData("http://192.168.1.20:5232/")]
    [InlineData("http://radicale:5232/")]
    [InlineData("http://radicale.lan/")]
    [InlineData("http://localhost:5232/")]
    [InlineData("http://10.0.0.5/dav/")]
    public void HttpOnTheLocalNetwork_Allowed(string url)
    {
        CalDavOptions options = Options([.. Valid(), ($"{Account}:url", url)]);

        Assert.Equal(new Uri(url), options.Accounts["nextcloud"].Url);
    }

    [Theory]
    [InlineData("url", "cloud.example.org/dav", "calendar.caldav.accounts.nextcloud.url")]
    [InlineData("url", "http://cloud.example.org/dav", "calendar.caldav.accounts.nextcloud.url")]
    [InlineData("url", "ftp://192.168.1.2/", "calendar.caldav.accounts.nextcloud.url")]
    [InlineData("username", "", "calendar.caldav.accounts.nextcloud.username")]
    [InlineData("passwordSecret", "", "calendar.caldav.accounts.nextcloud.passwordSecret")]
    [InlineData("passwordSecret", "Bad-Name", "calendar.caldav.accounts.nextcloud.passwordSecret")]
    [InlineData("calendars:0", " ", "calendar.caldav.accounts.nextcloud.calendars")]
    [InlineData("pollMinutes", "0", "calendar.caldav.accounts.nextcloud.pollMinutes")]
    [InlineData("pollMinutes", "61", "calendar.caldav.accounts.nextcloud.pollMinutes")]
    public void Invalid_FailsNamingTheKey(string key, string value, string named)
    {
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => Options([.. Valid(), ($"{Account}:{key}", value)]));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidAccountId_FailsNamingIt()
    {
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => Options(
        [
            ("calendar:caldav:accounts:Next-Cloud:url", "https://cloud.example.org/dav"),
            ("calendar:caldav:accounts:Next-Cloud:username", "lukas"),
            ("calendar:caldav:accounts:Next-Cloud:passwordSecret", "nextcloud_caldav"),
        ]));

        Assert.Contains("calendar.caldav.accounts.Next-Cloud", error.Message, StringComparison.Ordinal);
    }

    private static (string Key, string Value)[] Valid() =>
    [
        ($"{Account}:url", "https://cloud.example.org/remote.php/dav"),
        ($"{Account}:username", "lukas"),
        ($"{Account}:passwordSecret", "nextcloud_caldav"),
    ];

    private static CalDavOptions Options((string Key, string Value)[] values)
    {
        return Services(values).GetRequiredService<IOptions<CalDavOptions>>().Value;
    }

    private static ServiceProvider Services((string Key, string Value)[] values)
    {
        // Later values replace earlier ones, so a test can override a valid key.
        var settings = new Dictionary<string, string?>();
        foreach ((string key, string value) in values)
            settings[key] = value;
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ISecretStore>(new FakeSecretStore());
        services.AddSingleton<IEventPublisher>(new FakeEventPublisher());
        services.AddSingleton<IExecutionContextAccessor>(new FakeExecutionContextAccessor());
        services.AddSingleton<IInstanceOwner>(new FixedOwner());
        services.AddSingleton<ICalDavSyncStore>(new InMemorySyncStore());
        services.AddMarshalCalDav(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class FixedOwner : IInstanceOwner
    {
        public Task<OwnerId> GetAsync(CancellationToken cancellationToken) => Task.FromResult(TestExecutionContext.OwnerA);
    }

    private sealed class InMemorySyncStore : ICalDavSyncStore
    {
        public Task<CalDavCalendarSync?> GetAsync(string account, string calendar, CancellationToken cancellationToken) => Task.FromResult<CalDavCalendarSync?>(null);

        public Task SaveAsync(CalDavCalendarSync state, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
