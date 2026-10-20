using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.Mail.Accounts;
using Marshal.Integrations.Mail.Credentials;
using Marshal.Integrations.Mail.Imap;
using Marshal.Integrations.Mail.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Integrations.Mail;

/// <summary>
/// Registers mail: the account store, the IMAP provider with its credential sources, the OAuth clients
/// (<c>oauth</c> in <c>marshal.yaml</c>), the mail tools and the new-mail event source. There is no send path.
/// </summary>
public static class MailServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalMail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MailOAuthOptions>().Bind(configuration.GetSection("oauth"));
        services.AddHttpClient(OAuthTokenClient.HttpClientName);
        services.AddSingleton<OAuthTokenClient>();

        services.AddSingleton<ICredentialSource, PasswordCredentialSource>();
        foreach (OAuthPreset preset in OAuthPresets.All)
        {
            services.AddSingleton<ICredentialSource>(provider => new OAuth2CredentialSource(
                preset,
                provider.GetRequiredService<OAuthTokenClient>(),
                provider.GetRequiredService<ISecretStore>(),
                provider.GetRequiredService<ISecretWriter>(),
                provider.GetRequiredService<TimeProvider>()));
        }

        services.AddSingleton<IMailProvider, ImapMailProvider>();
        services.AddSingleton<IMailAccountStore, EfMailAccountStore>();
        services.AddSingleton<IToolSource, MailToolSource>();
        services.AddSingleton<IUntrustedContentResolver, MailContentResolver>();

        services.AddSingleton(new MailWatchTimings());
        services.AddSingleton<ImapNewMailSource>();
        services.AddSingleton<IEventSource>(provider => provider.GetRequiredService<ImapNewMailSource>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<ImapNewMailSource>());
        return services;
    }
}
