using System.Net.Http.Headers;

namespace Marshal.ExecutorHost.Callbacks;

/// <summary>
/// Posts session progress and completion to Marshal with the session's callback token.
/// </summary>
public interface ICallbackSender
{
    Task SendAsync(Uri url, string token, ExecutorCallback callback, CancellationToken cancellationToken);

    /// <summary>Uploads one artifact file below the callback URL; null when stored, else the reason it was refused.</summary>
    Task<string?> UploadAsync(Uri url, string token, string name, Stream content, CancellationToken cancellationToken);
}

/// <summary>A callback body.</summary>
/// <param name="Type"><c>progress</c>, <c>completed</c>, <c>failed</c> or <c>cancelled</c>.</param>
/// <param name="Message">Progress text or the failure.</param>
/// <param name="Branch">The session's branch (completed).</param>
/// <param name="Diff">Unified diff against the base branch (completed).</param>
/// <param name="Summary">Claude Code's final answer (completed).</param>
/// <param name="CostUsd">Reported model cost.</param>
public sealed record ExecutorCallback(string Type, string? Message = null, string? Branch = null, string? Diff = null, string? Summary = null, decimal? CostUsd = null);

/// <summary><see cref="ICallbackSender"/> over HTTP.</summary>
public sealed class HttpCallbackSender(IHttpClientFactory clients) : ICallbackSender
{
    public const string ClientName = "callbacks";

    public async Task SendAsync(Uri url, string token, ExecutorCallback callback, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(callback) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> UploadAsync(Uri url, string token, string name, Stream content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(url.AbsoluteUri.TrimEnd('/') + "/artifacts")) { Content = new StreamContent(content) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Artifact-Name", Uri.EscapeDataString(name));
        using HttpResponseMessage response = await clients.CreateClient(ClientName).SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return null;

        // Marshal answers refusals (size, quota) with a problem whose detail names the reason.
        try
        {
            ProblemBody? problem = await response.Content.ReadFromJsonAsync<ProblemBody>(cancellationToken);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
                return problem.Detail;
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return $"Marshal refused the upload ({(int)response.StatusCode}).";
    }

    private sealed record ProblemBody(string? Detail);
}
