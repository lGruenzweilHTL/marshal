using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.GitHub;

/// <summary>
/// The <c>repos.*</c> tools over GitHub, for the allowlisted repositories only: the <c>repo</c> argument is an enum of
/// configured ids, so other repositories cannot be named. All safe and read-only; every output is untrusted (titles,
/// bodies, comments, commit messages and logs are third-party text) and taints the run with kind <c>github</c>.
/// Diffs and failed jobs' logs are stored as tainted artifacts and reused for the same commit or job.
/// </summary>
public sealed partial class RepoToolSource(IGitHubClient client, IArtifactStore artifacts, IOptions<GitHubOptions> options) : IToolSource
{
    internal const int PullLimit = 30;
    internal const int IssueLimit = 30;
    internal const int RunLimit = 20;
    internal const int CommentLimit = 20;
    internal const int TailLines = 50;
    internal const int BodyLimit = 4000;
    internal const int CommentBodyLimit = 1000;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public string Name => "repos";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<GitHubRepo> repos = options.Value.Repositories();
        if (repos.Count == 0)
            return Task.FromResult<IReadOnlyList<ITool>>([]);

        var context = new RepoContext(client, artifacts, repos);
        return Task.FromResult<IReadOnlyList<ITool>>(
            [new PullsTool(context), new PullTool(context), new IssuesTool(context), new IssueTool(context), new RunsTool(context), new RunLogTool(context)]);
    }

    private sealed record RepoContext(IGitHubClient Client, IArtifactStore Artifacts, IReadOnlyList<GitHubRepo> Repos)
    {
        public ToolDescriptor Descriptor(string name, string description, string properties, params string[] required)
        {
            JsonObject schema = JsonNode.Parse("{\"type\":\"object\",\"properties\":{" + properties + "}}")!.AsObject();
            schema["properties"]!["repo"] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray([.. Repos.Select(repo => (JsonNode)JsonValue.Create(repo.Id))]),
                ["description"] = "Repository id: " + string.Join(", ", Repos.Select(repo => $"{repo.Id} = {repo.FullName}")),
            };
            schema["required"] = new JsonArray([JsonValue.Create("repo"), .. required.Select(item => (JsonNode)JsonValue.Create(item))]);
            return new ToolDescriptor
            {
                Id = new ToolId("repos", name),
                Description = description,
                ParametersSchema = JsonSerializer.SerializeToElement(schema),
                DefaultTier = RiskTier.Safe,
                UntrustedOutput = true,
                TaintKind = "github",
            };
        }

        public Result<GitHubRepo> Repo(JsonElement arguments) =>
            Text(arguments, "repo") is { } id && Repos.FirstOrDefault(repo => repo.Id == id) is { } repo
                ? repo
                : Invalid($"repo must be one of {string.Join(", ", Repos.Select(known => known.Id))}.");
    }

    private static ToolOutput Json(object value) => new(JsonSerializer.Serialize(value, _json));

    private static Error Invalid(string message) => new("repos.invalid_arguments", message, ErrorType.Validation);

    private static string? Text(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Result<long> Number(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long number) && number > 0
            ? number
            : Invalid($"{property} must be a positive whole number.");

    private static Result<string> State(JsonElement arguments)
    {
        string state = Text(arguments, "state") ?? "open";
        return state is "open" or "closed" or "all" ? state : Invalid("state must be open, closed or all.");
    }

    private static string? Cut(string? text, int limit) => text is null || text.Length <= limit ? text : text[..limit] + " …";

    private static string Time(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);

    private const string StateProperty = "\"state\":{\"type\":\"string\",\"enum\":[\"open\",\"closed\",\"all\"],\"description\":\"Default open.\"}";

    private const string NumberProperty = "\"number\":{\"type\":\"integer\",\"minimum\":1}";

    private sealed class PullsTool(RepoContext context) : ITool
    {
        public ToolDescriptor Descriptor { get; } = context.Descriptor("prs",
            "Lists up to 30 recently updated pull requests of a repository with number, title, author, draft flag and branches.",
            StateProperty);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<GitHubRepo> repo = context.Repo(invocation.Arguments);
            Result<string> state = State(invocation.Arguments);
            if (repo.IsFailure || state.IsFailure)
                return repo.IsFailure ? repo.Error : state.Error;

            Result<IReadOnlyList<GitHubPullSummary>> pulls = await context.Client.ListPullsAsync(repo.Value, state.Value, PullLimit, cancellationToken);
            return pulls.IsFailure ? pulls.Error : Json(pulls.Value.Select(pull => new
            {
                number = pull.Number, title = pull.Title, author = pull.Author, draft = pull.Draft, head = pull.Head, @base = pull.Base, updated = Time(pull.Updated),
            }));
        }
    }

    private sealed class PullTool(RepoContext context) : ITool
    {
        public ToolDescriptor Descriptor { get; } = context.Descriptor("pr",
            "Shows a pull request: title, author, state, branches, description and change counts. Its diff is kept as an artifact (diffArtifactId) that artifacts_show can open.",
            NumberProperty, "number");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<GitHubRepo> repo = context.Repo(invocation.Arguments);
            Result<long> number = Number(invocation.Arguments, "number");
            if (repo.IsFailure || number.IsFailure)
                return repo.IsFailure ? repo.Error : number.Error;

            Result<GitHubPull> found = await context.Client.GetPullAsync(repo.Value, (int)number.Value, cancellationToken);
            if (found.IsFailure)
                return found.Error;

            GitHubPull pull = found.Value;
            string reference = $"{repo.Value.Id}/pr/{pull.Number}@{pull.HeadSha}";
            (string? artifactId, string? notStored) = await StoreAsync(
                context, repo.Value, reference, $"{repo.Value.Id}-pr-{pull.Number}.diff", GitHubDownload.PullDiff, pull.Number, tail: null, cancellationToken);
            return Json(new
            {
                number = pull.Number, title = pull.Title, author = pull.Author, state = pull.State, draft = pull.Draft, merged = pull.Merged,
                head = pull.Head, @base = pull.Base, body = Cut(pull.Body, BodyLimit),
                changedFiles = pull.ChangedFiles, additions = pull.Additions, deletions = pull.Deletions,
                diffStored = artifactId is not null, diffArtifactId = artifactId, diffNotStored = notStored,
            });
        }
    }

    private sealed class IssuesTool(RepoContext context) : ITool
    {
        public ToolDescriptor Descriptor { get; } = context.Descriptor("issues",
            "Lists up to 30 newest issues (no pull requests) with number, title, author, labels and comment count. query filters titles among the 100 newest.",
            StateProperty + ",\"query\":{\"type\":\"string\",\"description\":\"Text the title must contain.\"}");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<GitHubRepo> repo = context.Repo(invocation.Arguments);
            Result<string> state = State(invocation.Arguments);
            if (repo.IsFailure || state.IsFailure)
                return repo.IsFailure ? repo.Error : state.Error;

            Result<IReadOnlyList<GitHubIssueSummary>> issues = await context.Client.ListIssuesAsync(repo.Value, state.Value, cancellationToken);
            if (issues.IsFailure)
                return issues.Error;

            string? query = Text(invocation.Arguments, "query");
            return Json(issues.Value
                .Where(issue => string.IsNullOrWhiteSpace(query) || issue.Title.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(IssueLimit)
                .Select(issue => new { number = issue.Number, title = issue.Title, author = issue.Author, labels = issue.Labels, comments = issue.Comments, updated = Time(issue.Updated) }));
        }
    }

    private sealed class IssueTool(RepoContext context) : ITool
    {
        public ToolDescriptor Descriptor { get; } = context.Descriptor("issue",
            "Shows an issue: title, author, state, labels, description and the 20 newest comments.",
            NumberProperty, "number");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<GitHubRepo> repo = context.Repo(invocation.Arguments);
            Result<long> number = Number(invocation.Arguments, "number");
            if (repo.IsFailure || number.IsFailure)
                return repo.IsFailure ? repo.Error : number.Error;

            Result<GitHubIssue> found = await context.Client.GetIssueAsync(repo.Value, (int)number.Value, cancellationToken);
            if (found.IsFailure)
                return found.Error;

            GitHubIssue issue = found.Value;
            Result<IReadOnlyList<GitHubComment>> comments = await context.Client.ListCommentsAsync(repo.Value, issue.Number, issue.Comments, CommentLimit, cancellationToken);
            if (comments.IsFailure)
                return comments.Error;

            return Json(new
            {
                number = issue.Number, title = issue.Title, author = issue.Author, state = issue.State, labels = issue.Labels, body = Cut(issue.Body, BodyLimit),
                commentCount = issue.Comments,
                comments = comments.Value.Select(comment => new { author = comment.Author, created = Time(comment.Created), body = Cut(comment.Body, CommentBodyLimit) }),
            });
        }
    }

    private sealed class RunsTool(RepoContext context) : ITool
    {
        public ToolDescriptor Descriptor { get; } = context.Descriptor("runs",
            "Lists up to 20 newest CI workflow runs with id, workflow, title, branch, event, status, conclusion and commit.",
            "\"branch\":{\"type\":\"string\"}");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<GitHubRepo> repo = context.Repo(invocation.Arguments);
            if (repo.IsFailure)
                return repo.Error;

            Result<IReadOnlyList<GitHubRun>> runs = await context.Client.ListRunsAsync(repo.Value, Text(invocation.Arguments, "branch"), RunLimit, cancellationToken);
            return runs.IsFailure ? runs.Error : Json(runs.Value.Select(run => new
            {
                id = run.Id, workflow = run.Workflow, title = run.Title, branch = run.Branch, @event = run.Event, status = run.Status, conclusion = run.Conclusion,
                commit = run.Sha.Length > 7 ? run.Sha[..7] : run.Sha, created = Time(run.Created),
            }));
        }
    }

    private sealed class RunLogTool(RepoContext context) : ITool
    {
        public ToolDescriptor Descriptor { get; } = context.Descriptor("run_log",
            "Explains why a CI run failed: for each failed job its failed steps and the last 50 log lines. The full log is kept as an artifact (logArtifactId).",
            "\"runId\":{\"type\":\"integer\",\"minimum\":1}", "runId");

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<GitHubRepo> repo = context.Repo(invocation.Arguments);
            Result<long> runId = Number(invocation.Arguments, "runId");
            if (repo.IsFailure || runId.IsFailure)
                return repo.IsFailure ? repo.Error : runId.Error;

            Result<IReadOnlyList<GitHubJob>> jobs = await context.Client.ListJobsAsync(repo.Value, runId.Value, cancellationToken);
            if (jobs.IsFailure)
                return jobs.Error;

            var failed = new List<object>();
            foreach (GitHubJob job in jobs.Value.Where(job => job.Conclusion is "failure" or "timed_out"))
            {
                var tail = new List<string>();
                (string? artifactId, string? notStored) = await StoreAsync(
                    context, repo.Value, $"{repo.Value.Id}/job/{job.Id}", $"{repo.Value.Id}-run-{runId.Value}-{Slug(job.Name)}.log", GitHubDownload.JobLog, job.Id, tail, cancellationToken);
                failed.Add(new
                {
                    job = job.Name,
                    conclusion = job.Conclusion,
                    failedSteps = job.Steps.Where(step => step.Conclusion == "failure").Select(step => step.Name),
                    logTail = tail,
                    logStored = artifactId is not null,
                    logArtifactId = artifactId,
                    logNotStored = notStored,
                });
            }

            return Json(new { runId = runId.Value, failedJobs = failed });
        }

        private static string Slug(string name) => SlugCharacters().Replace(name.ToLowerInvariant(), "-").Trim('-') is { Length: > 0 } slug ? slug : "job";
    }

    // Reuses an artifact with the same reference; otherwise downloads into the store. With a tail list, the last lines
    // are collected from the same read (or from the stored artifact on reuse).
    private static async Task<(string? ArtifactId, string? NotStored)> StoreAsync(
        RepoContext context, GitHubRepo repo, string reference, string name, GitHubDownload download, long id, List<string>? tail, CancellationToken cancellationToken)
    {
        if (await context.Artifacts.FindBySourceAsync(ArtifactSource.Github, reference, cancellationToken) is { } existing)
        {
            if (tail is not null && (await context.Artifacts.OpenReadAsync(existing.Id, cancellationToken)) is { IsSuccess: true } opened)
            {
                await using var reading = new TailingStream(opened.Value.Content);
                await reading.DrainAsync(cancellationToken);
                tail.AddRange(reading.Tail(TailLines));
            }

            return (existing.Id.ToString(), null);
        }

        Result<ArtifactRecord>? created = null;
        Result downloaded = await context.Client.DownloadAsync(repo, download, id, async (content, ct) =>
        {
            await using var reading = new TailingStream(content);
            created = await context.Artifacts.CreateAsync(new ArtifactDraft(name, ArtifactSource.Github, reference, Tainted: true), reading, ct);
            await reading.DrainAsync(ct);
            tail?.AddRange(reading.Tail(TailLines));
        }, cancellationToken);

        if (downloaded.IsFailure)
            return (null, downloaded.Error.Message);
        return created is { IsSuccess: true } stored ? (stored.Value.Id.ToString(), null) : (null, created?.Error.Message ?? "Nothing was downloaded.");
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugCharacters();
}
