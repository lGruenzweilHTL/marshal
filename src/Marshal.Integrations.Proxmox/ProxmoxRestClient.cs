using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Secrets;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Proxmox;

/// <summary>
/// The Proxmox VE REST API (<c>/api2/json</c>) with an API token header (<c>PVEAPIToken=&lt;id&gt;=&lt;value&gt;</c>).
/// The token value is read from the secret store per request, so a replaced secret needs no restart.
/// </summary>
public sealed class ProxmoxRestClient(IHttpClientFactory httpClients, IOptions<ProxmoxOptions> options, ISecretStore secrets) : IProxmoxClient
{
    public const string HttpClientName = "proxmox";

    public async Task<Result<ProxmoxResources>> GetResourcesAsync(CancellationToken cancellationToken)
    {
        Result<JsonElement> data = await GetAsync("cluster/resources", cancellationToken);
        if (data.IsFailure)
            return data.Error;

        List<ProxmoxNode> nodes = [];
        List<ProxmoxGuest> guests = [];
        List<ProxmoxStorage> storages = [];
        foreach (JsonElement entry in data.Value.EnumerateArray())
        {
            switch (Text(entry, "type"))
            {
                case "node":
                    nodes.Add(new ProxmoxNode(Text(entry, "node") ?? "", Text(entry, "status") == "online", Number(entry, "cpu"), Integer(entry, "mem"), Integer(entry, "maxmem"), Integer(entry, "uptime")));
                    break;
                case "qemu" or "lxc":
                    guests.Add(new ProxmoxGuest(
                        (int)Integer(entry, "vmid"), Text(entry, "name") ?? "", Text(entry, "type")!, Text(entry, "node") ?? "", Text(entry, "status") ?? "unknown",
                        Number(entry, "cpu"), Integer(entry, "mem"), Integer(entry, "maxmem"), Integer(entry, "uptime"), Integer(entry, "template") == 1));
                    break;
                case "storage":
                    storages.Add(new ProxmoxStorage(Text(entry, "storage") ?? "", Text(entry, "node") ?? "", Text(entry, "plugintype") ?? "", Integer(entry, "disk"), Integer(entry, "maxdisk"), Integer(entry, "shared") == 1));
                    break;
            }
        }

        return new ProxmoxResources(nodes, guests, storages);
    }

    public async Task<Result<IReadOnlyList<ProxmoxTask>>> GetTasksAsync(CancellationToken cancellationToken)
    {
        Result<JsonElement> data = await GetAsync("cluster/tasks", cancellationToken);
        if (data.IsFailure)
            return data.Error;

        List<ProxmoxTask> tasks = [.. data.Value.EnumerateArray().Select(entry => new ProxmoxTask(
            Text(entry, "upid") ?? "",
            Text(entry, "type") ?? "",
            Text(entry, "id") is { Length: > 0 } id ? id : null,
            Text(entry, "node") ?? "",
            Text(entry, "user") ?? "",
            DateTimeOffset.FromUnixTimeSeconds(Integer(entry, "starttime")),
            entry.TryGetProperty("endtime", out JsonElement end) && end.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(end.GetInt64()) : null,
            Text(entry, "status")))];
        return tasks;
    }

    private async Task<Result<JsonElement>> GetAsync(string path, CancellationToken cancellationToken)
    {
        ProxmoxOptions settings = options.Value;
        if (settings.Url is not { } url)
            return Unavailable("Proxmox is not configured.");
        if (!SecretName.IsValid(settings.TokenSecret))
            return Unavailable("proxmox.tokenSecret is not set.");

        Result<string> token = await secrets.GetAsync(SecretName.Create(settings.TokenSecret).Value, cancellationToken);
        if (token.IsFailure)
            return Unavailable($"The Proxmox token secret '{settings.TokenSecret}' is not set.");

        string root = url.ToString().TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{root}/api2/json/{path}"));
        request.Headers.TryAddWithoutValidation("Authorization", $"PVEAPIToken={settings.TokenId}={token.Value.Trim()}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using HttpClient http = httpClients.CreateClient(HttpClientName);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Unavailable($"Proxmox rejected the API token ({(int)response.StatusCode}); check the token and its PVEAuditor role.");
            if (!response.IsSuccessStatusCode)
                return Unavailable($"Proxmox answered {(int)response.StatusCode} for {path}.");

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array
                ? data.Clone()
                : Unavailable($"Proxmox sent an unexpected answer for {path}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Unavailable($"Proxmox is not reachable: {exception.Message}");
        }
    }

    private static Error Unavailable(string message) => new("infra.unavailable", message, ErrorType.Unavailable);

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        } : null;

    private static double Number(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static long Integer(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? (long)value.GetDouble() : 0;
}
