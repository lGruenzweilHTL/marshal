using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Keeps one websocket per configured printer: identifies as an agent, subscribes to the status objects Marshal
/// shows, merges <c>notify_status_update</c> diffs and re-reads everything after Klipper restarts or a reconnect.
/// A disconnected printer reads as offline until the reconnect has loaded a fresh state, so callers never act on
/// stale data.
/// </summary>
public sealed partial class MoonrakerStateCache(
    IOptions<PrinterOptions> options,
    IMoonrakerSocketFactory sockets,
    MoonrakerTimings timings,
    TimeProvider time,
    ILogger<MoonrakerStateCache> logger) : BackgroundService, IPrinterStateCache
{
    /// <summary>Client name sent in <c>server.connection.identify</c>.</summary>
    public const string ClientName = "Marshal";

    private static readonly string[] _objects = ["print_stats", "virtual_sdcard", "extruder", "heater_bed", "webhooks", "display_status"];

    private readonly ConcurrentDictionary<string, PrinterState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action<string, PrinterState?>> _subscribers = [];

    public Result<PrinterState> Get(string printer)
    {
        if (!options.Value.Printers.Exists(config => string.Equals(config.Name, printer, StringComparison.OrdinalIgnoreCase)))
            return PrinterErrors.NotFound(printer);

        return _states.TryGetValue(printer, out PrinterState? state) ? state : PrinterErrors.Offline(printer);
    }

    public IDisposable Subscribe(Action<string, PrinterState?> changed)
    {
        lock (_subscribers)
            _subscribers.Add(changed);

        return new Subscription(this, changed);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(options.Value.Printers.Where(printer => printer.Url is not null).Select(printer => KeepConnectedAsync(printer, stoppingToken)));

    private async Task KeepConnectedAsync(PrinterConfig printer, CancellationToken stoppingToken)
    {
        int attempt = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using MoonrakerConnection connection = await MoonrakerConnection.ConnectAsync(sockets, printer.BaseUri(), printer.ApiKey, timings.RequestTimeout, stoppingToken);
                await RunAsync(printer, connection, () => attempt = 0, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogDisconnected(printer.Name, exception.Message);
            }

            if (_states.TryRemove(printer.Name, out _))
                Notify(printer.Name, null);

            try
            {
                await Task.Delay(timings.Backoff(attempt++), time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _states.TryRemove(printer.Name, out _);
    }

    private async Task RunAsync(PrinterConfig printer, MoonrakerConnection connection, Action connected, CancellationToken stoppingToken)
    {
        await connection.CallAsync("server.connection.identify", new JsonObject
        {
            ["client_name"] = ClientName,
            ["version"] = typeof(MoonrakerStateCache).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            ["type"] = "agent",
            ["url"] = "https://github.com/lGruenzweilHTL/marshal",
            ["api_key"] = printer.ApiKey,
        }, stoppingToken);

        JsonElement info = await connection.CallAsync("server.info", null, stoppingToken);
        string klippy = info.TryGetProperty("klippy_state", out JsonElement found) ? found.GetString() ?? "disconnected" : "disconnected";
        JsonObject status = klippy == "ready" ? await SubscribeAsync(connection, stoppingToken) : KlippyOnly(klippy);
        Publish(printer.Name, status);
        connected();
        LogConnected(printer.Name, klippy);

        await foreach ((string method, JsonElement parameters) in connection.Notifications.ReadAllAsync(stoppingToken))
        {
            switch (method)
            {
                case "notify_status_update" when parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0:
                    Merge(status, JsonNode.Parse(parameters[0].GetRawText())!.AsObject());
                    break;
                case "notify_klippy_ready":
                    status = await SubscribeAsync(connection, stoppingToken);
                    break;
                case "notify_klippy_shutdown":
                    Merge(status, KlippyOnly("shutdown"));
                    break;
                case "notify_klippy_disconnected":
                    Merge(status, KlippyOnly("disconnected"));
                    break;
                default:
                    continue;
            }

            Publish(printer.Name, status);
        }

        throw new MoonrakerException("The connection to Moonraker was closed.");
    }

    private static async Task<JsonObject> SubscribeAsync(MoonrakerConnection connection, CancellationToken cancellationToken)
    {
        var objects = new JsonObject();
        foreach (string name in _objects)
            objects[name] = null;

        JsonElement result = await connection.CallAsync("printer.objects.subscribe", new JsonObject { ["objects"] = objects }, cancellationToken);
        return result.TryGetProperty("status", out JsonElement status) && status.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(status.GetRawText())!.AsObject()
            : [];
    }

    private static JsonObject KlippyOnly(string state) => new() { ["webhooks"] = new JsonObject { ["state"] = state } };

    private static void Merge(JsonObject target, JsonObject diff)
    {
        foreach ((string key, JsonNode? value) in diff.ToList())
        {
            if (value is JsonObject nested && target[key] is JsonObject existing)
            {
                Merge(existing, nested);
            }
            else
            {
                diff.Remove(key);
                target[key] = value;
            }
        }
    }

    private void Publish(string printer, JsonObject status)
    {
        PrinterState state = Map(printer, status, time.GetUtcNow());
        _states[printer] = state;
        Notify(printer, state);
    }

    private void Notify(string printer, PrinterState? state)
    {
        Action<string, PrinterState?>[] subscribers;
        lock (_subscribers)
            subscribers = [.. _subscribers];

        foreach (Action<string, PrinterState?> subscriber in subscribers)
        {
            try
            {
                subscriber(printer, state);
            }
            catch (Exception exception)
            {
                LogSubscriberFailed(exception);
            }
        }
    }

    internal static PrinterState Map(string printer, JsonObject status, DateTimeOffset now)
    {
        JsonNode? job = status["print_stats"];
        JsonNode? display = status["display_status"];
        string printState = Text(job?["state"]) ?? "standby";
        double progress = Number(display?["progress"]) ?? Number(status["virtual_sdcard"]?["progress"]) ?? 0;
        double printDuration = Number(job?["print_duration"]) ?? 0;
        double? eta = printState == "printing" && progress > 0 ? Math.Round(printDuration / progress - printDuration) : null;

        return new PrinterState(
            printer,
            Text(status["webhooks"]?["state"]) ?? "disconnected",
            Text(status["webhooks"]?["state_message"]),
            printState,
            Text(job?["filename"]),
            progress,
            printDuration,
            Number(job?["total_duration"]) ?? 0,
            Number(job?["filament_used"]) ?? 0,
            Integer(job?["info"]?["current_layer"]),
            Integer(job?["info"]?["total_layer"]),
            eta,
            Text(job?["message"]) ?? Text(display?["message"]),
            Heater(status["extruder"]),
            Heater(status["heater_bed"]),
            now);
    }

    private static HeaterState Heater(JsonNode? heater) =>
        new(Number(heater?["temperature"]) ?? 0, Number(heater?["target"]) ?? 0, Number(heater?["power"]) ?? 0);

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrEmpty(text) ? text : null;

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number ? value.GetValue<double>() : null;

    private static int? Integer(JsonNode? node) => Number(node) is { } number ? (int)number : null;

    private void Unsubscribe(Action<string, PrinterState?> changed)
    {
        lock (_subscribers)
            _subscribers.Remove(changed);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Printer {Printer} connected (Klippy {KlippyState})")]
    private partial void LogConnected(string printer, string klippyState);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Printer {Printer} connection lost: {Reason}")]
    private partial void LogDisconnected(string printer, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A printer state subscriber failed")]
    private partial void LogSubscriberFailed(Exception exception);

    private sealed class Subscription(MoonrakerStateCache cache, Action<string, PrinterState?> changed) : IDisposable
    {
        public void Dispose() => cache.Unsubscribe(changed);
    }
}
