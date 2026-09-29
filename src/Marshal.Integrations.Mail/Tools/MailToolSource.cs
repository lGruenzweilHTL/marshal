using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.Mail.Tools;

/// <summary>The <c>mail.*</c> tools (a built-in source). They read and draft; nothing sends, deletes or moves mail.</summary>
public sealed class MailToolSource(IMailAccountStore accounts, IEnumerable<IMailProvider> providers) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = MailTools.Create(accounts, [.. providers]);

    public string Name => "mail";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
