using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Auth;
using Marshal.Data.Audit;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.Api.Tests.Http;

public sealed class PrinterApiTests : IAsyncLifetime
{
    private const string Voron = "/api/v1/printers/voron";

    private readonly FakePrinterClient _client = new();
    private readonly FakePrinterStateCache _states = new();
    private readonly FakeWebcams _webcams = new();
    private readonly FakeSnapshotSource _snapshots = new();
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["printers:0:name"] = "voron";
        settings["printers:0:url"] = "http://voron.invalid:7125";
        settings["printers:0:macros:0"] = "PARK";
        _factory = new MarshalApiFactory(settings)
        {
            TestServices = services =>
            {
                services.RemoveAll<IPrinterClient>();
                services.AddSingleton<IPrinterClient>(_client);
                services.RemoveAll<IPrinterStateCache>();
                services.AddSingleton<IPrinterStateCache>(_states);
                services.RemoveAll<IPrinterWebcams>();
                services.AddSingleton<IPrinterWebcams>(_webcams);
                services.RemoveAll<ISnapshotSource>();
                services.AddSingleton<ISnapshotSource>(provider =>
                {
                    _snapshots.Links = provider.GetRequiredService<ISnapshotLinks>();
                    return _snapshots;
                });
            },
        };
        _webcams.Webcams["voron"] = new PrinterWebcam("main", new Uri("http://voron.invalid/webcam/?action=snapshot"), new Uri("http://voron.invalid/webcam/?action=stream"));
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Status_FromTheStateCache()
    {
        _states.Set("voron", Printing());

        List<JsonElement> printers = (await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/printers", Ct))!;
        JsonElement status = await _browser.GetFromJsonAsync<JsonElement>(Voron, Ct);

        JsonElement voron = Assert.Single(printers);
        Assert.Equal(("voron", true), (voron.GetProperty("name").GetString(), voron.GetProperty("online").GetBoolean()));
        Assert.Equal(("printing", 0.25, 200.0, 30), (
            status.GetProperty("printState").GetString(),
            status.GetProperty("progress").GetDouble(),
            status.GetProperty("extruder").GetProperty("temperature").GetDouble(),
            status.GetProperty("currentLayer").GetInt32()));
    }

    [Fact]
    public async Task Status_OfflineAndUnknown_Problems()
    {
        HttpResponseMessage offline = await _browser.GetAsync(Voron, Ct);
        HttpResponseMessage unknown = await _browser.GetAsync("/api/v1/printers/prusa", Ct);
        List<JsonElement> printers = (await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/printers", Ct))!;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, offline.StatusCode);
        Assert.Contains("printer.offline", await offline.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.False(Assert.Single(printers).GetProperty("online").GetBoolean());
    }

    [Fact]
    public async Task FilesQueueAndHistory()
    {
        _client.Files = [new PrinterFile("benchy.gcode", DateTimeOffset.UnixEpoch, 2048)];
        _client.Queue = new JobQueueStatus("paused", [new QueuedJob("0001", "a.gcode", DateTimeOffset.UnixEpoch)]);
        _client.History = [new PrintJob("0002", "b.gcode", "completed", DateTimeOffset.UnixEpoch, null, 1, 2, 3, true)];

        JsonElement files = await _browser.GetFromJsonAsync<JsonElement>($"{Voron}/files", Ct);
        JsonElement queue = await _browser.GetFromJsonAsync<JsonElement>($"{Voron}/queue", Ct);
        JsonElement history = await _browser.GetFromJsonAsync<JsonElement>($"{Voron}/history?limit=5", Ct);

        Assert.Equal("benchy.gcode", files[0].GetProperty("path").GetString());
        Assert.Equal(("paused", "0001"), (queue.GetProperty("state").GetString(), queue.GetProperty("jobs")[0].GetProperty("jobId").GetString()));
        Assert.Equal("completed", history[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task SafeControl_ActsImmediatelyAndIsAudited()
    {
        JsonElement result = await ControlAsync(new { action = "pause" });

        Assert.Equal("done", result.GetProperty("status").GetString());
        Assert.Equal(new PrinterCommand.Pause(), Assert.Single(_client.Sent).Command);
        AuditEvent entry = Assert.Single(await AuditAsync());
        Assert.Equal(("voron", _user.Value.ToString()), (entry.Target, entry.ActorId));
        Assert.Contains("pause the print", entry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmControl_NeedsTheTokenFromTheDialog()
    {
        JsonElement pending = await ControlAsync(new { action = "start_print", file = "benchy.gcode" });

        Assert.Equal("confirmation_required", pending.GetProperty("status").GetString());
        Assert.Equal("start printing benchy.gcode", pending.GetProperty("description").GetString());
        Assert.Empty(_client.Sent);
        Assert.Empty(await AuditAsync());

        string token = pending.GetProperty("token").GetString()!;
        HttpResponseMessage confirmed = await _browser.PostAsJsonAsync($"{Voron}/controls/confirm", new { token }, Ct);
        HttpResponseMessage replayed = await _browser.PostAsJsonAsync($"{Voron}/controls/confirm", new { token }, Ct);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(new PrinterCommand.StartPrint("benchy.gcode"), Assert.Single(_client.Sent).Command);
        Assert.Contains("start printing benchy.gcode", Assert.Single(await AuditAsync()).Details, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, replayed.StatusCode);
    }

    [Fact]
    public async Task ConfirmToken_BoundToUserAndPrinter()
    {
        string token = (await ControlAsync(new { action = "set_temperature", heater = "bed", target = 60 })).GetProperty("token").GetString()!;
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        HttpResponseMessage otherUser = await other.PostAsJsonAsync($"{Voron}/controls/confirm", new { token }, Ct);
        HttpResponseMessage bogus = await _browser.PostAsJsonAsync($"{Voron}/controls/confirm", new { token = "not-a-token" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, otherUser.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bogus.StatusCode);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public async Task InvalidControl_ValidationProblemWithoutToken()
    {
        _client.Validation = command => command is PrinterCommand.RunMacro
            ? new Error("printer.macro_not_allowed", "not allowed", ErrorType.Validation)
            : Result.Success();

        HttpResponseMessage macro = await _browser.PostAsJsonAsync($"{Voron}/controls", new { action = "run_macro", macro = "SAVE_CONFIG" }, Ct);
        HttpResponseMessage unknown = await _browser.PostAsJsonAsync($"{Voron}/controls", new { action = "gcode", script = "M112" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, macro.StatusCode);
        Assert.Contains("printer.macro_not_allowed", await macro.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public async Task Controls_OnlyForSignedInUsers()
    {
        HttpClient token = await _factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        HttpResponseMessage withToken = await token.PostAsJsonAsync($"{Voron}/controls", new { action = "pause" }, Ct);
        HttpResponseMessage anonymous = await Browser.Create(_factory).PostAsJsonAsync($"{Voron}/controls", new { action = "pause" }, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, withToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public async Task Snapshot_CapturedAndServedFromMarshal()
    {
        HttpResponseMessage capture = await _browser.PostAsync($"{Voron}/snapshots", null, Ct);
        JsonElement snapshot = await capture.Content.ReadFromJsonAsync<JsonElement>(Ct);
        string url = snapshot.GetProperty("url").GetString()!;
        HttpResponseMessage image = await _browser.GetAsync(url, Ct);
        HttpResponseMessage anonymous = await Browser.Create(_factory).GetAsync(url, Ct);

        Assert.Equal($"{Voron}/snapshots/snap1", url);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(_snapshots.Jpeg, await image.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task WebcamStream_ProxiedForSignedInUsersOnly()
    {
        HttpResponseMessage stream = await _browser.GetAsync($"{Voron}/webcam/stream", Ct);
        HttpResponseMessage anonymous = await Browser.Create(_factory).GetAsync($"{Voron}/webcam/stream", Ct);

        Assert.Equal("multipart/x-mixed-replace", stream.Content.Headers.ContentType?.MediaType);
        Assert.Equal(_webcams.Stream.Body, await stream.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task LiveEvents_PrinterTopicCarriesStateChanges()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events?topics=printer");
        using HttpResponseMessage response = await _browser.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));

        _states.Set("voron", Printing());
        JsonElement state = await ReadEventAsync(reader, "printer.state");
        _states.Set("voron", null);
        JsonElement offline = await ReadEventAsync(reader, "printer.state");

        Assert.Equal(("voron", "printing"), (state.GetProperty("payload").GetProperty("printer").GetString(), state.GetProperty("payload").GetProperty("state").GetProperty("printState").GetString()));
        Assert.Equal(JsonValueKind.Null, offline.GetProperty("payload").GetProperty("state").ValueKind);
    }

    private async Task<JsonElement> ControlAsync(object body)
    {
        HttpResponseMessage response = await _browser.PostAsJsonAsync($"{Voron}/controls", body, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private Task<List<AuditEvent>> AuditAsync() =>
        _factory.WithDatabaseAsync(_user, db => db.AuditEvents.Where(entry => entry.Action == "printer.control").ToListAsync(Ct));

    private static PrinterState Printing() => new(
        "voron", "ready", null, "printing", "benchy.gcode", 0.25, 600, 720, 812.5, 30, 120, 1800, null,
        new HeaterState(200, 245, 1), new HeaterState(100, 100, 0.5), DateTimeOffset.UnixEpoch);

    private static async Task<JsonElement> ReadEventAsync(StreamReader reader, string type)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        string name = "message";
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            if (line.StartsWith("event:", StringComparison.Ordinal))
                name = line[6..].Trim();
            else if (line.StartsWith("data:", StringComparison.Ordinal) && name == type)
                return JsonDocument.Parse(line[5..]).RootElement.Clone();
        }

        throw new InvalidOperationException("The event stream ended.");
    }
}
