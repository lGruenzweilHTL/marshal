using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Secrets;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.GitHub;

/// <summary>
/// GitHub's REST API with a pinned API version. Requests only ever name an allowlisted repository. Log downloads
/// answer with a redirect to short-lived storage; that URL comes from GitHub, not the model, and is fetched by a
/// separate client without the token.
/// </summary>
public sealed class GitHubRestClient(IHttpClientFactory httpClients, IOptions<GitHubOptions> options, ISecretStore secrets, TimeProvider time) : IGitHubClient
{
    public const string HttpClientName = "github";
    public const string DownloadClientName = "github-download";

    /// <summary>The REST API version Marshal is written against.</summary>
    public const string ApiVersion = "2026-03-10";

    private static readonly Uri _api = new("https://api.github.com/");

    public async Task<Result<IReadOnlyList<GitHubPullSummary>>> ListPullsAsync(GitHubRepo repo, string state, int limit, CancellationToken cancellationToken)
    {
        Result<JsonElement> pulls = await GetJsonAsync(repo, $"pulls?state={state}&sort=updated&direction=desc&per_page={limit}", cancellationToken);
        return pulls.IsFailure ? pulls.Error : Result.Success<IReadOnlyList<GitHubPullSummary>>([.. pulls.Value.EnumerateArray().Select(pull => new GitHubPullSummary(
            Int(pull, "number"), Text(pull, "title") ?? "", Login(pull), Bool(pull, "draft"),
            Text(pull.GetProperty("head"), "ref") ?? "", Text(pull.GetProperty("base"), "ref") ?? "", Time(pull, "updated_at")))]);
    }

    public async Task<Result<GitHubPull>> GetPullAsync(GitHubRepo repo, int number, CancellationToken cancellationToken)
    {
        Result<JsonElement> found = await GetJsonAsync(repo, $"pulls/{number}", cancellationToken);
        if (found.IsFailure)
            return found.Error;

        JsonElement pull = found.Value;
        return new GitHubPull(
            Int(pull, "number"), Text(pull, "title") ?? "", Login(pull), Text(pull, "state") ?? "", Bool(pull, "draft"), Bool(pull, "merged"),
            Text(pull.GetProperty("head"), "ref") ?? "", Text(pull.GetProperty("head"), "sha") ?? "", Text(pull.GetProperty("base"), "ref") ?? "",
            Text(pull, "body"), Int(pull, "changed_files"), Int(pull, "additions"), Int(pull, "deletions"));
    }

    public async Task<Result<IReadOnlyList<GitHubIssueSummary>>> ListIssuesAsync(GitHubRepo repo, string state, CancellationToken cancellationToken)
    {
        Result<JsonElement> issues = await GetJsonAsync(repo, $"issues?state={state}&sort=created&direction=desc&per_page=100", cancellationToken);
        return issues.IsFailure ? issues.Error : Result.Success<IReadOnlyList<GitHubIssueSummary>>([.. issues.Value.EnumerateArray()
            .Where(issue => !issue.TryGetProperty("pull_request", out _))
            .Select(issue => new GitHubIssueSummary(Int(issue, "number"), Text(issue, "title") ?? "", Login(issue), Labels(issue), Int(issue, "comments"), Time(issue, "updated_at")))]);
    }

    public async Task<Result<GitHubIssue>> GetIssueAsync(GitHubRepo repo, int number, CancellationToken cancellationToken)
    {
        Result<JsonElement> found = await GetJsonAsync(repo, $"issues/{number}", cancellationToken);
        if (found.IsFailure)
            return found.Error;

        JsonElement issue = found.Value;
        if (issue.TryGetProperty("pull_request", out _))
            return NotFound($"#{number} in {repo.Id} is a pull request; use repos_pr.");

        return new GitHubIssue(Int(issue, "number"), Text(issue, "title") ?? "", Login(issue), Text(issue, "state") ?? "", Labels(issue), Text(issue, "body"), Int(issue, "comments"));
    }

    public async Task<Result<IReadOnlyList<GitHubComment>>> ListCommentsAsync(GitHubRepo repo, int number, int total, int limit, CancellationToken cancellationToken)
    {
        if (total <= 0)
            return Result.Success<IReadOnlyList<GitHubComment>>([]);

        // Comments come oldest first; the last page holds the newest.
        int page = Math.Max(1, (total + 99) / 100);
        Result<JsonElement> comments = await GetJsonAsync(repo, $"issues/{number}/comments?per_page=100&page={page}", cancellationToken);
        return comments.IsFailure ? comments.Error : Result.Success<IReadOnlyList<GitHubComment>>([.. comments.Value.EnumerateArray()
            .Select(comment => new GitHubComment(Login(comment), Time(comment, "created_at"), Text(comment, "body") ?? ""))
            .TakeLast(limit)]);
    }

    public async Task<Result<IReadOnlyList<GitHubRun>>> ListRunsAsync(GitHubRepo repo, string? branch, int limit, CancellationToken cancellationToken)
    {
        string query = branch is null ? "" : $"branch={Uri.EscapeDataString(branch)}&";
        Result<JsonElement> runs = await GetJsonAsync(repo, $"actions/runs?{query}per_page={limit}", cancellationToken);
        return runs.IsFailure ? runs.Error : Result.Success<IReadOnlyList<GitHubRun>>([.. runs.Value.GetProperty("workflow_runs").EnumerateArray().Select(run => new GitHubRun(
            run.GetProperty("id").GetInt64(), Text(run, "name") ?? "", Text(run, "display_title") ?? "", Text(run, "head_branch") ?? "", Text(run, "event") ?? "",
            Text(run, "status") ?? "", Text(run, "conclusion"), Text(run, "head_sha") ?? "", Time(run, "created_at")))]);
    }

    public async Task<Result<IReadOnlyList<GitHubJob>>> ListJobsAsync(GitHubRepo repo, long runId, CancellationToken cancellationToken)
    {
        Result<JsonElement> jobs = await GetJsonAsync(repo, $"actions/runs/{runId}/jobs?per_page=100", cancellationToken);
        return jobs.IsFailure ? jobs.Error : Result.Success<IReadOnlyList<GitHubJob>>([.. jobs.Value.GetProperty("jobs").EnumerateArray().Select(job => new GitHubJob(
            job.GetProperty("id").GetInt64(), Text(job, "name") ?? "", Text(job, "status") ?? "", Text(job, "conclusion"),
            job.TryGetProperty("steps", out JsonElement steps) && steps.ValueKind == JsonValueKind.Array
                ? [.. steps.EnumerateArray().Select(step => new GitHubStep(Text(step, "name") ?? "", Text(step, "conclusion")))]
                : []))]);
    }

    public async Task<Result> DownloadAsync(GitHubRepo repo, GitHubDownload download, long id, Func<Stream, CancellationToken, Task> read, CancellationToken cancellationToken)
    {
        string path = download == GitHubDownload.PullDiff ? $"pulls/{id}" : $"actions/jobs/{id}/logs";
        string accept = download == GitHubDownload.PullDiff ? "application/vnd.github.diff" : "application/vnd.github+json";
        try
        {
            Result<HttpResponseMessage> sent = await SendAsync(repo, path, accept, cancellationToken);
            if (sent.IsFailure)
                return sent.Error;

            using HttpResponseMessage response = sent.Value;
            if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.SeeOther
                && response.Headers.Location is { IsAbsoluteUri: true, Scheme: "https" } location)
            {
                using HttpClient storage = httpClients.CreateClient(DownloadClientName);
                using HttpResponseMessage stored = await storage.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!stored.IsSuccessStatusCode)
                    return Unavailable($"Downloading the log failed ({(int)stored.StatusCode}).");

                await using Stream content = await stored.Content.ReadAsStreamAsync(cancellationToken);
                await read(content, cancellationToken);
                return Result.Success();
            }

            if (!response.IsSuccessStatusCode)
                return Failure(response, repo, path);

            await using (Stream body = await response.Content.ReadAsStreamAsync(cancellationToken))
                await read(body, cancellationToken);
            return Result.Success();
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Unavailable($"GitHub is not reachable: {exception.Message}");
        }
    }

    private async Task<Result<JsonElement>> GetJsonAsync(GitHubRepo repo, string path, CancellationToken cancellationToken)
    {
        try
        {
            Result<HttpResponseMessage> sent = await SendAsync(repo, path, "application/vnd.github+json", cancellationToken);
            if (sent.IsFailure)
                return sent.Error;

            using HttpResponseMessage response = sent.Value;
            if (!response.IsSuccessStatusCode)
                return Failure(response, repo, path);

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Unavailable($"GitHub is not reachable: {exception.Message}");
        }
    }

    // GET only; the response body is read by the caller (headers-read, so downloads stream).
    private async Task<Result<HttpResponseMessage>> SendAsync(GitHubRepo repo, string path, string accept, CancellationToken cancellationToken)
    {
        string? secret = options.Value.TokenSecret;
        if (!SecretName.IsValid(secret))
            return Unavailable("github.tokenSecret is not set.");

        Result<string> token = await secrets.GetAsync(SecretName.Create(secret).Value, cancellationToken);
        if (token.IsFailure)
            return Unavailable($"The GitHub token secret '{secret}' is not set.");

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_api, $"repos/{Uri.EscapeDataString(repo.Owner)}/{Uri.EscapeDataString(repo.Name)}/{path}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value.Trim());
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);
        request.Headers.UserAgent.ParseAdd("Marshal");
        HttpClient http = httpClients.CreateClient(HttpClientName);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private Error Failure(HttpResponseMessage response, GitHubRepo repo, string path)
    {
        if (response.StatusCode == HttpStatusCode.NotFound)
            return NotFound($"GitHub has nothing at {path} in {repo.Id}.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return Unavailable("GitHub rejected the token; it may have expired.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests
            && Header(response, "x-ratelimit-remaining") == "0"
            && long.TryParse(Header(response, "x-ratelimit-reset"), NumberStyles.None, CultureInfo.InvariantCulture, out long reset))
        {
            var at = DateTimeOffset.FromUnixTimeSeconds(reset);
            int minutes = Math.Max(1, (int)Math.Ceiling((at - time.GetUtcNow()).TotalMinutes));
            return Unavailable(string.Create(CultureInfo.InvariantCulture, $"GitHub's rate limit is used up until {at:O} (about {minutes} min)."));
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
            return Unavailable($"GitHub refused access to {repo.Id}; check the token's repositories and permissions.");
        return Unavailable($"GitHub answered {(int)response.StatusCode} for {path} in {repo.Id}.");
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.FirstOrDefault() : null;

    private static Error NotFound(string message) => new("repos.not_found", message, ErrorType.NotFound);

    private static Error Unavailable(string message) => new("repos.unavailable", message, ErrorType.Unavailable);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset Time(JsonElement element, string name) =>
        Text(element, name) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed) ? parsed : DateTimeOffset.UnixEpoch;

    private static string Login(JsonElement element) =>
        element.TryGetProperty("user", out JsonElement user) && user.ValueKind == JsonValueKind.Object ? Text(user, "login") ?? "" : "";

    private static IReadOnlyList<string> Labels(JsonElement issue) =>
        issue.TryGetProperty("labels", out JsonElement labels) && labels.ValueKind == JsonValueKind.Array
            ? [.. labels.EnumerateArray().Select(label => Text(label, "name")).OfType<string>()]
            : [];
}
