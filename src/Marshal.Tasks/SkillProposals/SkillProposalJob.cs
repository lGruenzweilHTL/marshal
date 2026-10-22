using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Suggestions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Suggestions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.SkillProposals;

/// <summary>
/// Nightly system job <c>skill_proposals</c>: for every owner with "help Marshal learn" on, finds workflows repeated in
/// at least three ok, not down-rated runs of the last 30 days (equal tool sequence, similar requests) and has a
/// <c>standard</c> tier model draft a <c>SKILL.md</c> from the user requests, tool names and argument names, never
/// from tool results or argument values. A workflow the model finds covered by an existing skill, or one proposed or
/// dismissed before, is not drafted again. New drafts are summarized in one <c>low</c> notification per owner.
/// </summary>
public sealed partial class SkillProposalJob(
    IUserDirectory users,
    IUserPreferencesStore preferences,
    IWorkflowRunReader runs,
    ISkillProposalStore proposals,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    BackgroundModelCalls calls,
    ISkillCatalog skills,
    INotificationSender notifications,
    IExecutionContextAccessor executionContext,
    TimeProvider time,
    ILogger<SkillProposalJob> logger) : ISystemJob
{
    public const string JobName = "skill_proposals";

    /// <summary>Run trigger of the drafting calls.</summary>
    public const string Trigger = "skill_proposal";

    /// <summary>Notification source of the nightly summary.</summary>
    public const string Source = "skill_proposals";

    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    public const int MinToolCalls = 3;

    private const int MaxInputLength = 300;

    private static readonly JsonElement _schema = BackgroundModelCalls.Schema("""
        {"type":"object","properties":{
          "covered":{"type":"boolean"},
          "name":{"type":"string"},
          "description":{"type":"string"},
          "body":{"type":"string"}},
         "required":["covered","name","description","body"],"additionalProperties":false}
        """);

    public string Name => JobName;

    public async Task<Result<string>> RunAsync(CancellationToken cancellationToken)
    {
        int drafted = 0;
        foreach (OwnerId owner in await users.ListAsync(cancellationToken))
        {
            UserPreferencesRecord prefs = await preferences.GetAsync(owner, cancellationToken);
            if (!prefs.HelpMarshalLearn)
                continue;

            using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(owner) with { Language = prefs.Language });
            try
            {
                drafted += await RunForOwnerAsync(owner, prefs.Language, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogOwnerFailed(exception, owner);
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{drafted} skill proposal(s) drafted.");
    }

    private async Task<int> RunForOwnerAsync(OwnerId owner, string language, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkflowRun> candidates = await runs.ListAsync(time.GetUtcNow() - Window, MinToolCalls, cancellationToken);
        if (candidates.Count < WorkflowGrouper.DefaultMinRuns)
            return 0;

        candidates = await WithEmbeddingsAsync(candidates, cancellationToken);
        var drafted = new List<SkillProposalRecord>();
        foreach (WorkflowGroup group in WorkflowGrouper.Group(candidates))
        {
            if (await proposals.HasSequenceAsync(group.SequenceHash, cancellationToken))
                continue;

            if (await DraftAsync(group, cancellationToken) is { } proposal)
            {
                SkillProposalRecord stored = await proposals.AddAsync(proposal, cancellationToken);
                if (stored.Status == SkillProposalStatus.Open)
                    drafted.Add(stored);
            }
        }

        if (drafted.Count > 0)
            await NotifyAsync(owner, language, drafted, cancellationToken);
        return drafted.Count;
    }

    // Embeds the inputs that have no cached embedding (or one of another model's size) and caches them.
    private async Task<IReadOnlyList<WorkflowRun>> WithEmbeddingsAsync(IReadOnlyList<WorkflowRun> candidates, CancellationToken cancellationToken)
    {
        int? size = candidates.Select(run => run.Embedding?.Length).FirstOrDefault(length => length is > 0);
        WorkflowRun[] missing = [.. candidates.Where(run => run.Embedding is null || (size is { } expected && run.Embedding.Length != expected))];
        if (missing.Length == 0)
            return candidates;

        GeneratedEmbeddings<Embedding<float>> generated = await embeddings.GenerateAsync(missing.Select(run => run.Input), cancellationToken: cancellationToken);
        var fresh = new Dictionary<Abstractions.Runs.RunId, float[]>();
        for (int index = 0; index < missing.Length; index++)
        {
            float[] vector = generated[index].Vector.ToArray();
            fresh[missing[index].Id] = vector;
            await runs.SaveEmbeddingAsync(missing[index].Id, vector, cancellationToken);
        }

        return [.. candidates.Select(run => fresh.TryGetValue(run.Id, out float[]? vector) ? run with { Embedding = vector } : run)];
    }

    private async Task<NewSkillProposal?> DraftAsync(WorkflowGroup group, CancellationToken cancellationToken)
    {
        Result<JsonElement> answer = await calls.AskAsync(ModelTier.Standard, Trigger, Instructions, Input(group), _schema, "skill_draft", cancellationToken);
        if (answer.IsFailure)
        {
            LogNotDrafted(answer.Error.Code);
            return null;
        }

        JsonElement draft = answer.Value;
        var sources = group.Runs.Select(run => run.Id).ToList();
        if (draft.TryGetProperty("covered", out JsonElement covered) && covered.ValueKind == JsonValueKind.True)
            return new NewSkillProposal("", "", "", null, sources, group.SequenceHash, SkillProposalStatus.Covered);

        string name = Text(draft, "name").ToLowerInvariant();
        string description = Text(draft, "description").ReplaceLineEndings(" ");
        string body = Text(draft, "body");
        if (!SkillName().IsMatch(name) || description.Length == 0 || body.Length == 0)
        {
            LogInvalidDraft(name);
            return null;
        }

        string content = Content(name, description, body);
        string? diff = skills.Find(name) is { } existing
            ? UnifiedDiff.Create(Content(existing.Name, existing.Description, existing.Body), content, $"skills/{name}/SKILL.md") is { Length: > 0 } changed ? changed : null
            : null;
        return new NewSkillProposal(name, description, content, diff, sources, group.SequenceHash, SkillProposalStatus.Open);
    }

    private const string Instructions = """
        You turn a routine a user repeatedly walks a home assistant through into a skill: a Markdown instruction file the
        assistant loads when the routine comes up. You get the user's requests, the tools called in order with their
        argument names, and the existing skills. If an existing skill already covers this routine (by its name or
        description), answer covered: true with empty strings. Otherwise answer covered: false with:
        - name: lower case letters, digits and hyphens, starting with a letter (e.g. printer-warmup)
        - description: one sentence saying what the skill does and when to use it
        - body: Markdown with a short heading and numbered steps that name the tools in backticks and say which
          arguments to fill from the request. Do not invent tools or argument names.
        """;

    private string Input(WorkflowGroup group)
    {
        var input = new StringBuilder("User requests:\n");
        foreach (WorkflowRun run in group.Runs)
            input.Append("- ").Append(run.Input.Length > MaxInputLength ? run.Input[..MaxInputLength] + "…" : run.Input).Append('\n');

        input.Append("\nTools called, in order:\n");
        WorkflowRun first = group.Runs[0];
        for (int index = 0; index < first.Tools.Count; index++)
        {
            input.Append(CultureInfo.InvariantCulture, $"{index + 1}. {ModelName(first.Tools[index])}");
            IReadOnlyList<string> keys = first.ArgumentKeys.ElementAtOrDefault(index) ?? [];
            input.Append(keys.Count == 0 ? " (no arguments)" : $" (arguments: {string.Join(", ", keys)})").Append('\n');
        }

        input.Append("\nExisting skills:\n");
        if (skills.Skills.Count == 0)
            input.Append("(none)\n");
        foreach (Skill skill in skills.Skills)
            input.Append("- ").Append(skill.Name).Append(": ").Append(skill.Description).Append('\n');
        return input.ToString();
    }

    private async Task NotifyAsync(OwnerId owner, string language, List<SkillProposalRecord> drafted, CancellationToken cancellationToken)
    {
        string names = string.Join(", ", drafted.Select(proposal => proposal.Name));
        bool german = language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        Result<Guid> sent = await notifications.SendAsync(new NotificationRequest
        {
            Owner = owner,
            Source = Source,
            Priority = NotificationPriority.Low,
            Text = german
                ? $"Ich habe aus Abläufen, die du oft wiederholst, {drafted.Count} Skill-Entwurf/-Entwürfe erstellt: {names}. Du findest sie im Posteingang."
                : $"I drafted {drafted.Count} skill(s) from routines you repeat: {names}. Review them in the Inbox.",
            Link = "inbox",
        }, cancellationToken);
        if (sent.IsFailure)
            LogNotifyFailed(sent.Error.Message);
    }

    private static string Content(string name, string description, string body) =>
        $"---\nname: {name}\ndescription: {Yaml(description)}\n---\n\n{body.Trim()}\n";

    // A plain scalar unless the text would be read as YAML syntax.
    private static string Yaml(string text) =>
        text.IndexOfAny([':', '#', '\'', '"', '[', ']', '{', '}', '&', '*', '!', '|', '>', '%', '@', '`']) >= 0 || text.StartsWith('-')
            ? "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : text;

    private static string ModelName(string tool) => ToolId.Parse(tool) is { IsSuccess: true } id ? ToolRegistry.ModelName(id.Value) : tool;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";

    [GeneratedRegex("^[a-z][a-z0-9-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SkillName();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skill proposals failed for owner {Owner}")]
    private partial void LogOwnerFailed(Exception exception, OwnerId owner);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skill proposal not drafted: {Code}")]
    private partial void LogNotDrafted(string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skill draft with an invalid name or empty text dropped: '{Name}'")]
    private partial void LogInvalidDraft(string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skill proposal summary notification failed: {Reason}")]
    private partial void LogNotifyFailed(string reason);
}
