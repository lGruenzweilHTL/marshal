using Marshal.Abstractions;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Mail;

namespace Marshal.Integrations.Mail.Tools;

/// <summary>
/// <c>mail:&lt;alias&gt;:&lt;message id&gt;</c> for <c>agent.extract</c>: the message as <c>mail.read_message</c> renders it,
/// read through the current owner's accounts (taint source <c>mail</c>).
/// </summary>
public sealed class MailContentResolver(IMailAccountStore accounts, IEnumerable<IMailProvider> providers) : IUntrustedContentResolver
{
    private readonly IReadOnlyList<IMailProvider> _providers = [.. providers];

    public string Kind => "mail";

    public async ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => (await accounts.ListAsync(cancellationToken)).Count > 0;

    public async Task<Result<UntrustedItem>> ResolveAsync(string id, CancellationToken cancellationToken)
    {
        int colon = id.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == id.Length - 1)
            return NotFound(id);

        MailAccount? account = await accounts.FindByAliasAsync(id[..colon], cancellationToken);
        IMailProvider? provider = account is null ? null : _providers.FirstOrDefault(candidate => candidate.Kind == account.Provider);
        if (account is null || provider is null)
            return NotFound(id);

        Result<MailMessage> message = await provider.ReadMessageAsync(account, id[(colon + 1)..], cancellationToken);
        return message.IsSuccess
            ? new UntrustedItem($"{Kind}:{id}", MailTools.Render(account, message.Value), "mail")
            : NotFound(id);
    }

    private static Error NotFound(string id) => new(ExtractionAnswer.NotFoundCode, $"There is no mail message '{id}'.", ErrorType.NotFound);
}
