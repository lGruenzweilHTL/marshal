using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.Mail.Tools;

/// <summary>The <c>mail.*</c> tools (a built-in source). They read, draft and save attachments; nothing sends, deletes or moves mail.</summary>
public sealed class MailToolSource(IMailAccountStore accounts, IEnumerable<IMailProvider> providers, IArtifactStore artifacts) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = MailTools.Create(accounts, [.. providers], artifacts);

    public string Name => "mail";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("mail", "Email: searching and reading mail, mailboxes, drafting replies")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
