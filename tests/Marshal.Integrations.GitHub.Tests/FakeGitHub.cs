using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace Marshal.Integrations.GitHub.Tests;

/// <summary>
/// An in-process GitHub REST API for <c>lukas/marshal</c> in GitHub's response shapes, plus the log storage host
/// that job log downloads redirect to.
/// </summary>
internal sealed class FakeGitHub : IAsyncDisposable
{
    public const string Token = "github_pat_test";
    public const string Repo = "/repos/lukas/marshal";

    private readonly WebApplication _app;

    private FakeGitHub(WebApplication app) => _app = app;

    public ConcurrentQueue<(string Method, string Host, string PathAndQuery, string Authorization, string Accept, string ApiVersion)> Requests { get; } = new();

    /// <summary>Status to answer every API request with instead of the fixture (e.g. 404, 401, 403).</summary>
    public int? Status { get; set; }

    /// <summary>Headers sent with <see cref="Status"/>.</summary>
    public Dictionary<string, string> StatusHeaders { get; } = [];

    public string Diff { get; set; } = "diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1 +1 @@\n-old\n+new\n";

    public string Log { get; set; } = string.Concat(Enumerable.Range(1, 80).Select(line => $"2026-10-09T12:00:{line % 60:00}.1234567Z line {line}\n"));

    public string HeadSha { get; set; } = "abc1234def";

    public int IssueComments { get; set; } = 2;

    public TestServer Server => _app.GetTestServer();

    public static async Task<FakeGitHub> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        WebApplication app = builder.Build();
        var fake = new FakeGitHub(app);
        app.Use(async (context, next) =>
        {
            HttpRequest request = context.Request;
            fake.Requests.Enqueue((request.Method, request.Host.Host, request.Path + request.QueryString, request.Headers.Authorization.ToString(), request.Headers.Accept.ToString(), request.Headers["X-GitHub-Api-Version"].ToString()));
            if (request.Host.Host == "api.github.com" && request.Headers.Authorization != $"Bearer {Token}")
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (request.Host.Host == "api.github.com" && fake.Status is { } status)
            {
                context.Response.StatusCode = status;
                foreach ((string name, string value) in fake.StatusHeaders)
                    context.Response.Headers[name] = value;
                return;
            }

            await next(context);
        });

        app.MapGet($"{Repo}/pulls", () => Json("""
            [{"number":42,"title":"Add displays","user":{"login":"lukas"},"draft":false,"head":{"ref":"feat/displays","sha":"abc"},"base":{"ref":"master"},"updated_at":"2026-10-09T10:00:00Z"},
             {"number":41,"title":"Fix typo","user":{"login":"bot"},"draft":true,"head":{"ref":"fix/typo","sha":"def"},"base":{"ref":"master"},"updated_at":"2026-10-08T10:00:00Z"}]
            """));
        app.MapGet($"{Repo}/pulls/42", (HttpContext context) => context.Request.Headers.Accept.ToString().Contains("diff", StringComparison.Ordinal)
            ? Results.Text(fake.Diff, "text/plain")
            : Json($$"""
                {"number":42,"title":"Add displays","user":{"login":"lukas"},"state":"open","draft":false,"merged":false,
                 "head":{"ref":"feat/displays","sha":"{{fake.HeadSha}}"},"base":{"ref":"master"},"body":"Shows artifacts on TVs.","changed_files":3,"additions":120,"deletions":4}
                """));
        app.MapGet($"{Repo}/issues", () => Json("""
            [{"number":44,"title":"Printer page flickers","user":{"login":"anna"},"labels":[{"name":"bug"}],"comments":2,"updated_at":"2026-10-09T09:00:00Z"},
             {"number":42,"title":"Add displays","user":{"login":"lukas"},"labels":[],"comments":0,"updated_at":"2026-10-09T10:00:00Z","pull_request":{"url":"x"}},
             {"number":40,"title":"Dark mode","user":{"login":"anna"},"labels":[{"name":"ui"},{"name":"idea"}],"comments":0,"updated_at":"2026-10-01T09:00:00Z"}]
            """));
        app.MapGet($"{Repo}/issues/44", () => Json($$"""
            {"number":44,"title":"Printer page flickers","user":{"login":"anna"},"state":"open","labels":[{"name":"bug"}],"body":"{{new string('b', 5000)}}","comments":{{fake.IssueComments}}}
            """));
        app.MapGet($"{Repo}/issues/42", () => Json("""{"number":42,"title":"Add displays","user":{"login":"lukas"},"state":"open","labels":[],"comments":0,"pull_request":{"url":"x"}}"""));
        app.MapGet($"{Repo}/issues/44/comments", () => Json("""
            [{"user":{"login":"anna"},"created_at":"2026-10-09T09:05:00Z","body":"Happens on Firefox."},
             {"user":{"login":"lukas"},"created_at":"2026-10-09T09:10:00Z","body":"Confirmed."}]
            """));
        app.MapGet($"{Repo}/actions/runs", () => Json("""
            {"total_count":1,"workflow_runs":[{"id":7001,"name":"CI","display_title":"Add displays","head_branch":"feat/displays","event":"pull_request","status":"completed","conclusion":"failure","head_sha":"abc1234def5678","created_at":"2026-10-09T11:00:00Z"}]}
            """));
        app.MapGet($"{Repo}/actions/runs/7001/jobs", () => Json("""
            {"total_count":2,"jobs":[
              {"id":900,"name":"build (dotnet)","status":"completed","conclusion":"failure","steps":[{"name":"Checkout","conclusion":"success","number":1},{"name":"Test","conclusion":"failure","number":2}]},
              {"id":901,"name":"web","status":"completed","conclusion":"success","steps":[]}]}
            """));
        app.MapGet($"{Repo}/actions/jobs/900/logs", () => Results.Redirect("https://storage.test/logs/900?sig=abc"));
        app.MapGet("/logs/900", (HttpContext context) => context.Request.Host.Host == "storage.test" ? Results.Text(fake.Log, "text/plain") : Results.NotFound());
        await app.StartAsync();
        return fake;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static IResult Json(string body) => Results.Text(JsonNode.Parse(body)!.ToJsonString(), "application/json");
}
