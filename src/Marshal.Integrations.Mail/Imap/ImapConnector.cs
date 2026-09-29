using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Marshal.Abstractions;
using Marshal.Abstractions.Mail;

namespace Marshal.Integrations.Mail.Imap;

/// <summary>
/// Opens authenticated IMAP connections with the account's credential source.
/// </summary>
internal sealed class ImapConnector(IEnumerable<ICredentialSource> credentials)
{
    private const int TimeoutMilliseconds = 30_000;

    private readonly IReadOnlyList<ICredentialSource> _credentials = [.. credentials];

    /// <summary>A connected, authenticated client (the caller disposes it), or why there is none.</summary>
    public async Task<Result<ImapClient>> ConnectAsync(MailAccount account, CancellationToken cancellationToken)
    {
        ICredentialSource? source = _credentials.FirstOrDefault(candidate => candidate.Strategy == account.Strategy);
        if (source is null)
            return MailErrors.CredentialMissing(account.Alias);

        Result<MailCredential> credential = await source.GetAsync(account, cancellationToken);
        if (credential.IsFailure)
            return credential.Error;

        var client = new ImapClient { Timeout = TimeoutMilliseconds };
        try
        {
            await client.ConnectAsync(account.Settings.Host, account.Settings.Port, Security(account.Settings.Security), cancellationToken);
            if (credential.Value.IsAccessToken)
                await client.AuthenticateAsync(new SaslMechanismOAuth2(credential.Value.UserName, credential.Value.Secret), cancellationToken);
            else
                await client.AuthenticateAsync(credential.Value.UserName, credential.Value.Secret, cancellationToken);
            return client;
        }
        catch (AuthenticationException)
        {
            client.Dispose();
            return MailErrors.AuthFailed(account.Alias);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            client.Dispose();
            return MailErrors.Unreachable(account.Alias, exception.Message);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Errors that mean the server could not be reached or dropped the connection.</summary>
    public static bool IsConnectionFailure(Exception exception) =>
        exception is IOException or SocketException or ImapProtocolException or ImapCommandException or SslHandshakeException or ServiceNotConnectedException or TimeoutException;

    private static SecureSocketOptions Security(MailSecurity security) => security switch
    {
        MailSecurity.Tls => SecureSocketOptions.SslOnConnect,
        MailSecurity.StartTls => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.None,
    };
}
