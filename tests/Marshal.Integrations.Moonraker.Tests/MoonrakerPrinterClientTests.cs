using System.Net;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class MoonrakerPrinterClientTests : IAsyncLifetime
{
    private FakeMoonraker _fake = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _fake = await FakeMoonraker.StartAsync();

    public async ValueTask DisposeAsync() => await _fake.DisposeAsync();

    [Fact]
    public async Task Commands_SendTheApiKey()
    {
        Assert.True((await Client().SendAsync("voron", new PrinterCommand.Pause(), Ct)).IsSuccess);

        Assert.Equal(FakeMoonraker.ApiKey, Assert.Single(_fake.RestRequests).ApiKey);
    }

    [Fact]
    public async Task WrongApiKey_Unauthorized()
    {
        Result result = await Client(apiKey: "wrong").SendAsync("voron", new PrinterCommand.Pause(), Ct);

        Assert.Equal("printer.unauthorized", result.Error.Code);
    }

    [Theory]
    [InlineData("pause", "/printer/print/pause")]
    [InlineData("resume", "/printer/print/resume")]
    [InlineData("cancel", "/printer/print/cancel")]
    [InlineData("estop", "/printer/emergency_stop")]
    [InlineData("queue_pause", "/server/job_queue/pause")]
    [InlineData("queue_start", "/server/job_queue/start")]
    public async Task PrintControl_PostsTheEndpoint(string command, string path)
    {
        PrinterCommand typed = command switch
        {
            "pause" => new PrinterCommand.Pause(),
            "resume" => new PrinterCommand.Resume(),
            "cancel" => new PrinterCommand.Cancel(),
            "estop" => new PrinterCommand.EmergencyStop(),
            "queue_pause" => new PrinterCommand.QueuePause(),
            _ => new PrinterCommand.QueueStart(),
        };

        Assert.True((await Client().SendAsync("voron", typed, Ct)).IsSuccess);

        RestRequest request = Assert.Single(_fake.RestRequests);
        Assert.Equal(("POST", path), (request.Method, request.Path));
    }

    [Fact]
    public async Task StartPrint_FilenameInTheQuery()
    {
        await Client().SendAsync("voron", new PrinterCommand.StartPrint("parts/benchy v2.gcode"), Ct);

        RestRequest request = Assert.Single(_fake.RestRequests);
        Assert.Equal(("POST", "/printer/print/start", "?filename=parts%2Fbenchy%20v2.gcode"), (request.Method, request.Path, request.Query));
    }

    [Fact]
    public async Task Gcode_BuiltByMarshalFromTypedCommands()
    {
        PrinterCommand[] commands =
        [
            new PrinterCommand.SetTemperature(PrinterHeater.Extruder, 245),
            new PrinterCommand.SetTemperature(PrinterHeater.Bed, 0),
            new PrinterCommand.Home([]),
            new PrinterCommand.Home([PrinterAxis.X, PrinterAxis.Y]),
            new PrinterCommand.Move(PrinterAxis.Z, 10),
            new PrinterCommand.Move(PrinterAxis.X, -2.5),
            new PrinterCommand.RunMacro("park"),
        ];

        foreach (PrinterCommand command in commands)
            Assert.True((await Client().SendAsync("voron", command, Ct)).IsSuccess);

        Assert.All(_fake.RestRequests, request => Assert.Equal(("POST", "/printer/gcode/script"), (request.Method, request.Path)));
        Assert.Equal(
            [
                "SET_HEATER_TEMPERATURE HEATER=extruder TARGET=245",
                "SET_HEATER_TEMPERATURE HEATER=heater_bed TARGET=0",
                "G28",
                "G28 X Y",
                "G91\nG1 Z10\nG90",
                "G91\nG1 X-2.5\nG90",
                "PARK",
            ],
            _fake.RestRequests.Select(request => (string?)JsonNode.Parse(request.Body)!["script"]));
    }

    [Theory]
    [InlineData("FIRMWARE_RESTART", "printer.macro_not_allowed")]
    [InlineData("PARK X=1", "printer.macro_not_allowed")]
    [InlineData("PARK\nM112", "printer.macro_not_allowed")]
    public async Task Macro_OnlyAllowlistedNamesWithoutArguments(string macro, string code)
    {
        Result result = await Client().SendAsync("voron", new PrinterCommand.RunMacro(macro), Ct);

        Assert.Equal(code, result.Error.Code);
        Assert.Empty(_fake.RestRequests);
    }

    [Fact]
    public async Task InvalidArguments_RejectedBeforeSending()
    {
        PrinterCommand[] invalid =
        [
            new PrinterCommand.SetTemperature(PrinterHeater.Extruder, -5),
            new PrinterCommand.SetTemperature(PrinterHeater.Extruder, 400),
            new PrinterCommand.SetTemperature(PrinterHeater.Bed, 200),
            new PrinterCommand.SetTemperature(PrinterHeater.Extruder, double.NaN),
            new PrinterCommand.Move(PrinterAxis.X, 0),
            new PrinterCommand.Move(PrinterAxis.Z, 500),
            new PrinterCommand.StartPrint(""),
            new PrinterCommand.StartPrint("../etc/passwd"),
            new PrinterCommand.QueueAdd([]),
            new PrinterCommand.QueueRemove([]),
        ];

        foreach (PrinterCommand command in invalid)
            Assert.Equal("printer.invalid_command", (await Client().SendAsync("voron", command, Ct)).Error.Code);

        Assert.Empty(_fake.RestRequests);
    }

    [Fact]
    public async Task Queue_EditRequests()
    {
        await Client().SendAsync("voron", new PrinterCommand.QueueAdd(["a.gcode", "b.gcode"]), Ct);
        await Client().SendAsync("voron", new PrinterCommand.QueueRemove(["0001", "0002"]), Ct);
        await Client().SendAsync("voron", new PrinterCommand.QueueClear(), Ct);
        await Client().SendAsync("voron", new PrinterCommand.QueueJump("0003"), Ct);

        RestRequest[] requests = [.. _fake.RestRequests];
        Assert.Equal(("POST", "/server/job_queue/job"), (requests[0].Method, requests[0].Path));
        Assert.Equal(["a.gcode", "b.gcode"], JsonNode.Parse(requests[0].Body)!["filenames"]!.AsArray().Select(name => (string?)name));
        Assert.Equal(("DELETE", "/server/job_queue/job", "?job_ids=0001%2C0002"), (requests[1].Method, requests[1].Path, requests[1].Query));
        Assert.Equal(("DELETE", "/server/job_queue/job", "?all=true"), (requests[2].Method, requests[2].Path, requests[2].Query));
        Assert.Equal(("POST", "/server/job_queue/jump", "?job_id=0003"), (requests[3].Method, requests[3].Path, requests[3].Query));
    }

    [Fact]
    public async Task ListFiles_NewestFirst()
    {
        _fake.Rest["GET /server/files/list"] = JsonNode.Parse("""
            [{"path":"old.gcode","modified":1700000000.0,"size":100,"permissions":"rw"},
             {"path":"parts/new.gcode","modified":1700003600.5,"size":2048,"permissions":"rw"}]
            """);

        IReadOnlyList<PrinterFile> files = (await Client().ListFilesAsync("voron", Ct)).Value;

        Assert.Equal("?root=gcodes", Assert.Single(_fake.RestRequests).Query);
        Assert.Equal(["parts/new.gcode", "old.gcode"], files.Select(file => file.Path));
        Assert.Equal((2048L, DateTimeOffset.FromUnixTimeSeconds(1700003600).AddMilliseconds(500)), (files[0].Size, files[0].Modified));
    }

    [Fact]
    public async Task Queue_Parsed()
    {
        _fake.Rest["GET /server/job_queue/status"] = JsonNode.Parse("""
            {"queued_jobs":[{"filename":"a.gcode","job_id":"0000000066D991F0","time_added":1725534704.0,"time_in_queue":10.5}],"queue_state":"paused"}
            """);

        JobQueueStatus queue = (await Client().GetQueueAsync("voron", Ct)).Value;

        Assert.Equal("paused", queue.State);
        QueuedJob job = Assert.Single(queue.Jobs);
        Assert.Equal(("0000000066D991F0", "a.gcode", DateTimeOffset.FromUnixTimeSeconds(1725534704)), (job.JobId, job.Filename, job.AddedAt));
    }

    [Fact]
    public async Task History_Parsed()
    {
        _fake.Rest["GET /server/history/list"] = JsonNode.Parse("""
            {"count":2,"jobs":[
              {"job_id":"000002","exists":true,"end_time":null,"filament_used":10.0,"filename":"b.gcode","print_duration":60.0,"status":"in_progress","start_time":1700000100.0,"total_duration":70.0},
              {"job_id":"000001","exists":false,"end_time":1700000090.0,"filament_used":812.5,"filename":"a.gcode","print_duration":80.0,"status":"completed","start_time":1700000000.0,"total_duration":90.0}]}
            """);

        IReadOnlyList<PrintJob> jobs = (await Client().GetHistoryAsync("voron", 5, Ct)).Value;

        Assert.Equal("?limit=5&order=desc", Assert.Single(_fake.RestRequests).Query);
        Assert.Equal(["000002", "000001"], jobs.Select(job => job.JobId));
        Assert.Null(jobs[0].EndedAt);
        Assert.Equal(("completed", 812.5, false, DateTimeOffset.FromUnixTimeSeconds(1700000090)), (jobs[1].Status, jobs[1].FilamentUsed, jobs[1].Exists, jobs[1].EndedAt));
    }

    [Fact]
    public async Task MoonrakerError_ReturnedAsFailure()
    {
        _fake.RestErrors["POST /printer/print/resume"] = (HttpStatusCode.BadRequest, "Print is not paused");

        Result result = await Client().SendAsync("voron", new PrinterCommand.Resume(), Ct);

        Assert.Equal("printer.command_failed", result.Error.Code);
        Assert.Contains("Print is not paused", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownPrinter_NotFound()
    {
        Assert.Equal("printer.not_found", (await Client().SendAsync("prusa", new PrinterCommand.Pause(), Ct)).Error.Code);
    }

    [Fact]
    public async Task Unreachable_Offline()
    {
        var client = new MoonrakerPrinterClient(Options(FakeMoonraker.ApiKey), new SingleClientFactory(new HttpClient(new UnreachableHandler())));

        Assert.Equal("printer.offline", (await client.SendAsync("voron", new PrinterCommand.Pause(), Ct)).Error.Code);
    }

    private MoonrakerPrinterClient Client(string apiKey = FakeMoonraker.ApiKey) =>
        new(Options(apiKey), new SingleClientFactory(_fake.Server.CreateClient()));

    private Microsoft.Extensions.Options.IOptions<PrinterOptions> Options(string apiKey) =>
        Microsoft.Extensions.Options.Options.Create(new PrinterOptions
        {
            Printers = [new PrinterConfig { Name = "voron", Url = _fake.Server.BaseAddress, ApiKey = apiKey, Macros = ["PARK", "LOAD_FILAMENT"] }],
        });

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }
}
