using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.Mail.Tools;

/// <summary>
/// Model-facing mail tools. Message ids are <c>alias:providerId</c>, so reading and replying always hit the account
/// the message came from. Tools returning mail content are untrusted (taint source <c>mail</c>) and sensitive (the
/// run record keeps only the message references). Drafting is safe and never escalated: the user reviews and sends
/// drafts in their own mail client.
/// </summary>
internal static class MailTools
{
    private const string Taint = "mail";
    private const int MaxLimit = 50;

    // Non-ASCII stays readable for the model (folder names like "Entwürfe").
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public static IReadOnlyList<ITool> Create(IMailAccountStore accounts, IReadOnlyList<IMailProvider> providers)
    {
        var context = new MailToolContext(accounts, providers);
        return
        [
            new MailTool(context, "search", "Searches mail. Without account it searches every account; results carry the account alias and a message id.", """
                {"type":"object","properties":{
                  "account":{"type":"string","description":"Account alias, e.g. private or work; all accounts when omitted."},
                  "from":{"type":"string"},"to":{"type":"string"},"subject":{"type":"string"},
                  "text":{"type":"string","description":"Words in headers or body."},
                  "since":{"type":"string","format":"date","description":"Sent on or after (YYYY-MM-DD)."},
                  "before":{"type":"string","format":"date","description":"Sent before (YYYY-MM-DD)."},
                  "unread":{"type":"boolean"},
                  "folder":{"type":"string","description":"Folder (default: inbox)."},
                  "limit":{"type":"integer","minimum":1,"maximum":50}}}
                """, untrusted: true, SearchAsync),
            new MailTool(context, "read_message", "Reads one message as text (attachments by name only).", """
                {"type":"object","properties":{"id":{"type":"string","description":"Message id from mail_search."}},"required":["id"]}
                """, untrusted: true, ReadMessageAsync),
            new MailTool(context, "read_thread", "Reads the whole conversation a message belongs to, oldest first.", """
                {"type":"object","properties":{"id":{"type":"string","description":"Message id from mail_search."}},"required":["id"]}
                """, untrusted: true, ReadThreadAsync),
            new MailTool(context, "list_folders", "Lists the folders of a mail account.", """
                {"type":"object","properties":{"account":{"type":"string","description":"Account alias (default: the only account)."}}}
                """, untrusted: false, ListFoldersAsync),
            new MailTool(context, "list_labels", "Lists the labels of a mail account.", """
                {"type":"object","properties":{"account":{"type":"string","description":"Account alias (default: the only account)."}}}
                """, untrusted: false, ListLabelsAsync, MailCapabilities.Labels),
            new MailTool(context, "create_draft", "Saves a draft in the account's drafts folder for the user to review and send. Nothing is sent.", """
                {"type":"object","properties":{
                  "account":{"type":"string","description":"Account alias (default: the replied-to message's account, else the only account)."},
                  "to":{"type":"array","items":{"type":"string"},"description":"Recipient addresses."},
                  "subject":{"type":"string"},"body":{"type":"string","description":"Plain-text body."},
                  "replyTo":{"type":"string","description":"Id of the message this answers."}},
                 "required":["to","subject","body"]}
                """, untrusted: false, CreateDraftAsync),
            new MailTool(context, "update_draft", "Replaces a draft Marshal created with new content. Nothing is sent.", """
                {"type":"object","properties":{
                  "id":{"type":"string","description":"Draft id from mail_create_draft."},
                  "to":{"type":"array","items":{"type":"string"}},"subject":{"type":"string"},"body":{"type":"string"},
                  "replyTo":{"type":"string","description":"Id of the message this answers."}},
                 "required":["id","to","subject","body"]}
                """, untrusted: false, UpdateDraftAsync),
        ];
    }

    private static async Task<Result<ToolOutput>> SearchAsync(MailToolContext context, JsonElement arguments, CancellationToken ct)
    {
        IReadOnlyList<MailAccount> targets;
        if (Text(arguments, "account") is { } alias)
        {
            Result<MailAccount> account = await context.ResolveAsync(alias, ct);
            if (account.IsFailure)
                return account.Error;
            targets = [account.Value];
        }
        else
        {
            targets = await context.Accounts.ListAsync(ct);
        }

        if (!TryDate(arguments, "since", out DateOnly? since) || !TryDate(arguments, "before", out DateOnly? before))
            return Invalid("since and before must be dates like 2026-09-29.");

        var query = new MailQuery(
            Text(arguments, "from"), Text(arguments, "to"), Text(arguments, "subject"), Text(arguments, "text"), since, before,
            arguments.TryGetProperty("unread", out JsonElement unread) && unread.ValueKind is JsonValueKind.True or JsonValueKind.False ? unread.GetBoolean() : null,
            Text(arguments, "folder"),
            Math.Clamp(arguments.TryGetProperty("limit", out JsonElement limit) && limit.TryGetInt32(out int value) ? value : 20, 1, MaxLimit));

        var hits = new List<(MailAccount Account, MailSummary Summary)>();
        var failures = new List<string>();
        foreach (MailAccount account in targets)
        {
            Result<IReadOnlyList<MailSummary>> found = await context.Provider(account).SearchAsync(account, query, ct);
            if (found.IsSuccess)
                hits.AddRange(found.Value.Select(summary => (account, summary)));
            else
                failures.Add($"{account.Alias}: {found.Error.Message}");
        }

        if (hits.Count == 0 && failures.Count > 0 && failures.Count == targets.Count)
            return new Error("mail.search_failed", string.Join(" ", failures), ErrorType.Unavailable);

        List<(MailAccount Account, MailSummary Summary)> newest = [.. hits.OrderByDescending(hit => hit.Summary.Date).Take(query.Limit)];
        var result = newest.Select(hit => new
        {
            id = Id(hit.Account, hit.Summary.Id),
            account = hit.Account.Alias,
            hit.Summary.From,
            hit.Summary.Subject,
            hit.Summary.Date,
            hit.Summary.Unread,
            hit.Summary.Folder,
        });
        string content = JsonSerializer.Serialize(failures.Count == 0 ? (object)result : new { results = result, unavailable = failures }, _json);
        return new ToolOutput(content, string.Join(',', newest.Select(hit => Id(hit.Account, hit.Summary.Id))));
    }

    private static async Task<Result<ToolOutput>> ReadMessageAsync(MailToolContext context, JsonElement arguments, CancellationToken ct)
    {
        Result<(MailAccount Account, string ProviderId)> id = await context.ParseIdAsync(Text(arguments, "id"), ct);
        if (id.IsFailure)
            return id.Error;

        Result<MailMessage> message = await context.Provider(id.Value.Account).ReadMessageAsync(id.Value.Account, id.Value.ProviderId, ct);
        return message.Map(read => new ToolOutput(Render(id.Value.Account, read), Id(id.Value.Account, read.Id)));
    }

    private static async Task<Result<ToolOutput>> ReadThreadAsync(MailToolContext context, JsonElement arguments, CancellationToken ct)
    {
        Result<(MailAccount Account, string ProviderId)> id = await context.ParseIdAsync(Text(arguments, "id"), ct);
        if (id.IsFailure)
            return id.Error;

        MailAccount account = id.Value.Account;
        Result<IReadOnlyList<MailMessage>> thread = await context.Provider(account).ReadThreadAsync(account, id.Value.ProviderId, ct);
        return thread.Map(messages => new ToolOutput(
            string.Join("\n\n---\n\n", messages.Select(message => Render(account, message))),
            string.Join(',', messages.Select(message => Id(account, message.Id)))));
    }

    private static async Task<Result<ToolOutput>> ListFoldersAsync(MailToolContext context, JsonElement arguments, CancellationToken ct)
    {
        Result<MailAccount> account = await context.ResolveAsync(Text(arguments, "account"), ct);
        if (account.IsFailure)
            return account.Error;

        Result<IReadOnlyList<MailFolder>> folders = await context.Provider(account.Value).ListFoldersAsync(account.Value, ct);
        return folders.Map(list => new ToolOutput(JsonSerializer.Serialize(list.Select(folder => new { folder.Name, folder.SpecialUse }), _json)));
    }

    private static async Task<Result<ToolOutput>> ListLabelsAsync(MailToolContext context, JsonElement arguments, CancellationToken ct)
    {
        Result<MailAccount> account = await context.ResolveAsync(Text(arguments, "account"), ct);
        if (account.IsFailure)
            return account.Error;

        Result<IReadOnlyList<string>> labels = await context.Provider(account.Value).ListLabelsAsync(account.Value, ct);
        return labels.Map(list => new ToolOutput(JsonSerializer.Serialize(list, _json)));
    }

    private static async Task<Result<ToolOutput>> CreateDraftAsync(MailToolContext context, JsonElement arguments, CancellationToken ct)
    {
        (MailAccount Account, string ProviderId)? replyTo = null;
        if (Text(arguments, "replyTo") is { } replyId)
        {
            Result<(MailAccount Account, string ProviderId)> parsed = await context.ParseIdAsync(replyId, ct);
            if (parsed.IsFailure)
                return parsed.Error;
            replyTo = parsed.Value;
        }

        Result<MailAccount> account = Text(arguments, "account") is null && replyTo is { } parent
            ? parent.Account
            : await context.ResolveAsync(Text(arguments, "account"), ct);
        if (account.IsFailure)
            return account.Error;
        if (replyTo is { } answered && answered.Account.Id != account.Value.Id)
            return Invalid($"replyTo belongs to account '{answered.Account.Alias}'; draft the reply there.");

        Result<MailDraft> draft = Draft(arguments, replyTo?.ProviderId);
        if (draft.IsFailure)
            return draft.Error;

        Result<string> created = await context.Provider(account.Value).CreateDraftAsync(account.Value, draft.Value, ct);
        return created.Map(id => new ToolOutput($"Draft {Id(account.Value, id)} saved in {account.Value.Alias}; it was not sent."));
    }

    private static async Task<Result<ToolOutput>> UpdateDraftAsync(MailToolContext context, JsonElement arguments, CancellationToken ct)
    {
        Result<(MailAccount Account, string ProviderId)> id = await context.ParseIdAsync(Text(arguments, "id"), ct);
        if (id.IsFailure)
            return id.Error;

        string? replyProviderId = null;
        if (Text(arguments, "replyTo") is { } replyId)
        {
            Result<(MailAccount Account, string ProviderId)> parsed = await context.ParseIdAsync(replyId, ct);
            if (parsed.IsFailure)
                return parsed.Error;
            if (parsed.Value.Account.Id != id.Value.Account.Id)
                return Invalid("replyTo must be in the draft's account.");
            replyProviderId = parsed.Value.ProviderId;
        }

        Result<MailDraft> draft = Draft(arguments, replyProviderId);
        if (draft.IsFailure)
            return draft.Error;

        MailAccount account = id.Value.Account;
        Result<string> updated = await context.Provider(account).UpdateDraftAsync(account, id.Value.ProviderId, draft.Value, ct);
        return updated.Map(newId => new ToolOutput($"Draft updated; its id is now {Id(account, newId)}. It was not sent."));
    }

    private static Result<MailDraft> Draft(JsonElement arguments, string? replyTo)
    {
        string[] to = arguments.TryGetProperty("to", out JsonElement recipients) && recipients.ValueKind == JsonValueKind.Array
            ? [.. recipients.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!.Trim()).Where(item => item.Length > 0)]
            : [];
        if (to.Length == 0 || Text(arguments, "subject") is not { } subject || Text(arguments, "body") is not { } body)
            return Invalid("to, subject and body are required.");

        return new MailDraft(to, subject, body, replyTo);
    }

    private static string Render(MailAccount account, MailMessage message)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Id: {Id(account, message.Id)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Account: {account.Alias}");
        text.AppendLine(CultureInfo.InvariantCulture, $"From: {message.From}");
        text.AppendLine(CultureInfo.InvariantCulture, $"To: {string.Join(", ", message.To)}");
        if (message.Cc.Count > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Cc: {string.Join(", ", message.Cc)}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Date: {message.Date:yyyy-MM-dd HH:mm zzz}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Subject: {message.Subject}");
        if (message.Attachments.Count > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Attachments: {string.Join(", ", message.Attachments)}");
        text.AppendLine();
        text.Append(message.Body);
        return text.ToString();
    }

    private static string Id(MailAccount account, string providerId) => $"{account.Alias}:{providerId}";

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;

    private static bool TryDate(JsonElement arguments, string name, out DateOnly? date)
    {
        date = null;
        if (Text(arguments, name) is not { } text)
            return true;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed))
            return false;
        date = parsed;
        return true;
    }

    private static Error Invalid(string message) => new("mail.invalid_arguments", message, ErrorType.Validation);

    private sealed class MailToolContext(IMailAccountStore accounts, IReadOnlyList<IMailProvider> providers)
    {
        public IMailAccountStore Accounts => accounts;

        public IReadOnlyList<IMailProvider> Providers => providers;

        public IMailProvider Provider(MailAccount account) => providers.First(provider => provider.Kind == account.Provider);

        /// <summary>The named account, or the only one when no alias is given.</summary>
        public async Task<Result<MailAccount>> ResolveAsync(string? alias, CancellationToken ct)
        {
            if (alias is not null)
            {
                return await accounts.FindByAliasAsync(alias, ct) is { } found
                    ? found
                    : new Error("mail.account_not_found", $"There is no mail account '{alias}'; accounts: {string.Join(", ", (await accounts.ListAsync(ct)).Select(account => account.Alias))}.", ErrorType.NotFound);
            }

            IReadOnlyList<MailAccount> all = await accounts.ListAsync(ct);
            return all.Count == 1
                ? all[0]
                : Invalid($"Name the account: {string.Join(", ", all.Select(account => account.Alias))}.");
        }

        public async Task<Result<(MailAccount Account, string ProviderId)>> ParseIdAsync(string? id, CancellationToken ct)
        {
            int colon = id?.IndexOf(':', StringComparison.Ordinal) ?? -1;
            if (id is null || colon <= 0 || colon == id.Length - 1)
                return MailErrors.MessageNotFound(id ?? "");

            return await accounts.FindByAliasAsync(id[..colon], ct) is { } account
                ? (account, id[(colon + 1)..])
                : MailErrors.MessageNotFound(id);
        }
    }

    private sealed class MailTool(
        MailToolContext context,
        string name,
        string description,
        string schema,
        bool untrusted,
        Func<MailToolContext, JsonElement, CancellationToken, Task<Result<ToolOutput>>> run,
        MailCapabilities requires = MailCapabilities.None) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("mail", name),
            Description = description,
            ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
            DefaultTier = RiskTier.Safe,
            UntrustedOutput = untrusted,
            TaintKind = untrusted ? Taint : null,
            Sensitive = untrusted,
        };

        public async ValueTask<bool> IsAvailableAsync(MarshalExecutionContext executionContext, CancellationToken cancellationToken)
        {
            IReadOnlyList<MailAccount> accounts = await context.Accounts.ListAsync(cancellationToken);
            return accounts.Any(account => requires == MailCapabilities.None || context.Provider(account).Capabilities.HasFlag(requires));
        }

        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken) =>
            run(context, invocation.Arguments.ValueKind == JsonValueKind.Object ? invocation.Arguments : JsonDocument.Parse("{}").RootElement, cancellationToken);
    }
}
