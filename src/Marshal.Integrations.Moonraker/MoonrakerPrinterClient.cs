using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Moonraker's REST API with the printer's <c>X-Api-Key</c>. Print control uses the dedicated endpoints; heating,
/// homing, moving and macros go through <c>/printer/gcode/script</c> with scripts from
/// <see cref="PrinterCommandRules"/> only.
/// </summary>
public sealed class MoonrakerPrinterClient(IOptions<PrinterOptions> options, IHttpClientFactory httpClients) : IPrinterClient
{
    /// <summary>Name of the <see cref="HttpClient"/> (resilience pipeline configured by the host).</summary>
    public const string HttpClientName = "moonraker";

    public async Task<Result<IReadOnlyList<PrinterFile>>> ListFilesAsync(string printer, CancellationToken cancellationToken)
    {
        Result<JsonElement> result = await RequestAsync(printer, HttpMethod.Get, "server/files/list?root=gcodes", null, cancellationToken);
        if (result.IsFailure)
            return result.Error;

        List<PrinterFile> files = [.. result.Value.EnumerateArray().Select(file => new PrinterFile(
            Text(file, "path") ?? "",
            Time(file, "modified") ?? DateTimeOffset.UnixEpoch,
            file.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long bytes) ? bytes : 0))];
        return files.OrderByDescending(file => file.Modified).ToList();
    }

    public async Task<Result<JobQueueStatus>> GetQueueAsync(string printer, CancellationToken cancellationToken)
    {
        Result<JsonElement> result = await RequestAsync(printer, HttpMethod.Get, "server/job_queue/status", null, cancellationToken);
        if (result.IsFailure)
            return result.Error;

        List<QueuedJob> jobs = result.Value.TryGetProperty("queued_jobs", out JsonElement queued)
            ? [.. queued.EnumerateArray().Select(job => new QueuedJob(Text(job, "job_id") ?? "", Text(job, "filename") ?? "", Time(job, "time_added") ?? DateTimeOffset.UnixEpoch))]
            : [];
        return new JobQueueStatus(Text(result.Value, "queue_state") ?? "ready", jobs);
    }

    public async Task<Result<IReadOnlyList<PrintJob>>> GetHistoryAsync(string printer, int limit, CancellationToken cancellationToken)
    {
        Result<JsonElement> result = await RequestAsync(printer, HttpMethod.Get, $"server/history/list?limit={Math.Clamp(limit, 1, 100)}&order=desc", null, cancellationToken);
        if (result.IsFailure)
            return result.Error;

        List<PrintJob> jobs = result.Value.TryGetProperty("jobs", out JsonElement list)
            ? [.. list.EnumerateArray().Select(job => new PrintJob(
                Text(job, "job_id") ?? "",
                Text(job, "filename") ?? "",
                Text(job, "status") ?? "unknown",
                Time(job, "start_time") ?? DateTimeOffset.UnixEpoch,
                Time(job, "end_time"),
                Number(job, "print_duration"),
                Number(job, "total_duration"),
                Number(job, "filament_used"),
                job.TryGetProperty("exists", out JsonElement exists) && exists.ValueKind == JsonValueKind.True))]
            : [];
        return jobs;
    }

    public async Task<Result> SendAsync(string printer, PrinterCommand command, CancellationToken cancellationToken)
    {
        if (Find(printer) is not { } config)
            return PrinterErrors.NotFound(printer);

        var info = new PrinterInfo(config.Name, [.. config.Macros.Select(macro => macro.ToUpperInvariant())]);
        Result valid = PrinterCommandRules.Validate(command, info);
        if (valid.IsFailure)
            return valid;

        (HttpMethod method, string path, JsonObject? body) = PrinterCommandRules.Script(command) is { } script
            ? (HttpMethod.Post, "printer/gcode/script", new JsonObject { ["script"] = script })
            : Endpoint(command);
        Result<JsonElement> result = await RequestAsync(printer, method, path, body, cancellationToken);
        return result.IsFailure ? result.Error : Result.Success();
    }

    private static (HttpMethod Method, string Path, JsonObject? Body) Endpoint(PrinterCommand command) => command switch
    {
        PrinterCommand.StartPrint start => (HttpMethod.Post, $"printer/print/start?filename={Uri.EscapeDataString(start.File)}", null),
        PrinterCommand.Pause => (HttpMethod.Post, "printer/print/pause", null),
        PrinterCommand.Resume => (HttpMethod.Post, "printer/print/resume", null),
        PrinterCommand.Cancel => (HttpMethod.Post, "printer/print/cancel", null),
        PrinterCommand.EmergencyStop => (HttpMethod.Post, "printer/emergency_stop", null),
        PrinterCommand.QueueAdd add => (HttpMethod.Post, "server/job_queue/job", new JsonObject { ["filenames"] = new JsonArray([.. add.Files.Select(file => (JsonNode)file)]) }),
        PrinterCommand.QueueRemove remove => (HttpMethod.Delete, $"server/job_queue/job?job_ids={Uri.EscapeDataString(string.Join(',', remove.JobIds))}", null),
        PrinterCommand.QueueClear => (HttpMethod.Delete, "server/job_queue/job?all=true", null),
        PrinterCommand.QueuePause => (HttpMethod.Post, "server/job_queue/pause", null),
        PrinterCommand.QueueStart => (HttpMethod.Post, "server/job_queue/start", null),
        PrinterCommand.QueueJump jump => (HttpMethod.Post, $"server/job_queue/jump?job_id={Uri.EscapeDataString(jump.JobId)}", null),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown printer command."),
    };

    private async Task<Result<JsonElement>> RequestAsync(string printer, HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        if (Find(printer) is not { } config)
            return PrinterErrors.NotFound(printer);

        using var request = new HttpRequestMessage(method, new Uri(config.BaseUri(), path));
        if (config.ApiKey is not null)
            request.Headers.Add("X-Api-Key", config.ApiKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        try
        {
            using HttpResponseMessage response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return PrinterErrors.Unauthorized(config.Name);

            using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;
            if (!response.IsSuccessStatusCode || !root.TryGetProperty("result", out JsonElement result))
            {
                string message = root.TryGetProperty("error", out JsonElement error) && Text(error, "message") is { } text ? text : $"HTTP {(int)response.StatusCode}";
                return PrinterErrors.CommandFailed(config.Name, message);
            }

            return result.Clone();
        }
        catch (HttpRequestException)
        {
            return PrinterErrors.Offline(config.Name);
        }
        catch (JsonException)
        {
            return PrinterErrors.CommandFailed(config.Name, "Moonraker sent an unreadable answer.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PrinterErrors.Offline(config.Name);
        }
    }

    private PrinterConfig? Find(string printer) =>
        options.Value.Printers.FirstOrDefault(config => config.Url is not null && string.Equals(config.Name, printer, StringComparison.OrdinalIgnoreCase));

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;

    private static DateTimeOffset? Time(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(value.GetDouble() * 1000))
            : null;
}
