using System.Net.Http.Headers;

namespace Marshal.ExecutorHost.Callbacks;

/// <summary>
/// Posts session progress and completion to Marshal with the session's callback token.
/// </summary>
public interface ICallbackSender
{
    Task SendAsync(Uri url, string token, ExecutorCallback callback, CancellationToken cancellationToken);
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
}
