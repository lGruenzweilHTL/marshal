using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Secrets;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Executors;

/// <summary>
/// <see cref="IExecutor"/> <c>claude_code</c>: a Claude Code session on <c>Marshal.ExecutorHost</c> (its own worktree
/// and branch in a sandbox container), started over HTTP with the host token from the secret store. The spec names
/// a configured repository and the prompt; the result is a branch with a diff for review.
/// </summary>
public sealed class ClaudeCodeExecutor(IHttpClientFactory clients, ISecretStore secrets, IOptions<ExecutorOptions> options) : IExecutor
{
    public const string ClientName = "executor-host";

    private const int MaxPromptLength = 20_000;

    private static readonly HashSet<string> _fields = new(StringComparer.Ordinal) { "repository", "prompt", "baseBranch" };

    private ClaudeCodeOptions Settings => options.Value.ClaudeCode;

    public string Name => "claude_code";

    public string Description =>
        "Claude Code working on a git repository in a sandbox; the result is a branch with a diff the user reviews and merges in the web UI. "
        + "Spec: {repository, prompt, baseBranch?}. Repositories: "
        + string.Join("; ", Settings.Repositories.Select(repository => repository.Description is null ? repository.Name : $"{repository.Name} ({repository.Description})"))
        + ".";

    public Result ValidateSpec(JsonElement spec)
    {
        if (spec.ValueKind != JsonValueKind.Object)
            return Invalid("The spec must be an object {repository, prompt, baseBranch?}.");
        foreach (JsonProperty property in spec.EnumerateObject())
        {
            if (!_fields.Contains(property.Name))
                return Invalid($"Unknown spec field '{property.Name}'.");
        }

        string? repository = Text(spec, "repository");
        if (repository is null || !Settings.Repositories.Any(known => string.Equals(known.Name, repository, StringComparison.Ordinal)))
            return Invalid($"Unknown repository. Known: {string.Join(", ", Settings.Repositories.Select(known => known.Name))}.");
        string? prompt = Text(spec, "prompt");
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaxPromptLength)
            return Invalid($"The prompt must be between 1 and {MaxPromptLength} characters.");
        if (spec.TryGetProperty("baseBranch", out JsonElement baseBranch) && baseBranch.ValueKind != JsonValueKind.String)
            return Invalid("baseBranch must be a branch name.");
        return Result.Success();
    }

    public async Task<Result<ExecutorStarted>> StartAsync(ExecutorStart start, CancellationToken cancellationToken)
    {
        string repository = Text(start.Spec, "repository")!;
        var body = new
        {
            repository,
            prompt = Text(start.Spec, "prompt"),
            baseBranch = Text(start.Spec, "baseBranch"),
            callbackUrl = start.CallbackUrl.ToString(),
            callbackToken = start.CallbackToken,
        };

        Result<HttpResponseMessage> sent = await SendAsync(HttpMethod.Post, "sessions", JsonContent.Create(body), cancellationToken);
        if (sent.IsFailure)
            return sent.Error;

        using HttpResponseMessage response = sent.Value;
        if (!response.IsSuccessStatusCode)
            return StartFailed($"The executor host refused the session ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}", ErrorType.Failure);

        JsonElement started = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return Text(started, "id") is { } id
            ? new ExecutorStarted(id, repository)
            : StartFailed("The executor host returned no session id.", ErrorType.Failure);
    }

    public async Task<Result> CancelAsync(string handle, CancellationToken cancellationToken)
    {
        Result<HttpResponseMessage> sent = await SendAsync(HttpMethod.Delete, $"sessions/{Uri.EscapeDataString(handle)}", null, cancellationToken);
        if (sent.IsFailure)
            return sent.Error;

        using HttpResponseMessage response = sent.Value;
        return response.IsSuccessStatusCode
            ? Result.Success()
            : new Error("executor.cancel_failed", $"The executor host could not cancel the session ({(int)response.StatusCode}).", ErrorType.Failure);
    }

    private async Task<Result<HttpResponseMessage>> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        if (Settings.Url is not { } url || !SecretName.IsValid(Settings.TokenSecret))
            return StartFailed("executors.claudeCode is not configured.", ErrorType.Unavailable);
        Result<string> token = await secrets.GetAsync(SecretName.Create(Settings.TokenSecret).Value, cancellationToken);
        if (token.IsFailure)
            return StartFailed($"The executor host token '{Settings.TokenSecret}' is not set.", ErrorType.Unavailable);

        using var request = new HttpRequestMessage(method, new Uri(url, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        try
        {
            return await clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return StartFailed($"The executor host is unreachable: {exception.Message}", ErrorType.Unavailable);
        }
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Error Invalid(string message) => new("executor.invalid_spec", message, ErrorType.Validation);

    private static Error StartFailed(string message, ErrorType type) => new("executor.start_failed", message, type);
}
