using System.Collections;
using System.Net;
using System.Net.Sockets;
using Marshal.Api.Hosting;

namespace Marshal.Api.Tests.Hosting;

public sealed class MarshalHostTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("marshal-host-").FullName;

    [Fact]
    public async Task Run_InvalidConfiguration_ReportsErrorsWithoutStackTraceAndReturnsOne()
    {
        Hashtable environment = ValidEnvironment();
        environment["MARSHAL__Embedding__Model"] = "";
        var report = new StringWriter();

        int exit = await MarshalHost.RunAsync([], environment, report);

        Assert.Equal(1, exit);
        Assert.Contains("  MARSHAL__Embedding__Model  embedding.model  is required.", report.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_InvalidConfigurationAtDebugLevel_IncludesStackTrace()
    {
        Hashtable environment = ValidEnvironment();
        environment["MARSHAL__Embedding__Model"] = "";
        environment["MARSHAL__Logging__LogLevel__Default"] = "Debug";
        var report = new StringWriter();

        int exit = await MarshalHost.RunAsync([], environment, report);

        Assert.Equal(1, exit);
        Assert.Contains("   at ", report.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_PortInUse_ReportsSummaryAndStackTraceAndReturnsOne()
    {
        using var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Start();
        Hashtable environment = ValidEnvironment();
        environment["MARSHAL__Http__Urls"] = $"http://127.0.0.1:{((IPEndPoint)occupied.LocalEndpoint).Port}";
        var report = new StringWriter();

        int exit = await MarshalHost.RunAsync([], environment, report);

        Assert.Equal(1, exit);
        string[] lines = report.ToString().ReplaceLineEndings("\n").Split('\n');
        Assert.StartsWith("marshal: startup failed: ", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith("   at ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_WithoutReportWriter_PropagatesTheFailure()
    {
        Hashtable environment = ValidEnvironment();
        environment["MARSHAL__Embedding__Model"] = "";

        await Assert.ThrowsAsync<StartupFailedException>(() => MarshalHost.RunAsync([], environment, report: null));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private Hashtable ValidEnvironment()
    {
        string config = Directory.CreateDirectory(Path.Combine(_root, "config")).FullName;
        return new Hashtable
        {
            ["MARSHAL__Paths__Config"] = config,
            ["MARSHAL__Paths__Data"] = Path.Combine(_root, "data"),
            ["MARSHAL__Paths__Secrets"] = Directory.CreateDirectory(Path.Combine(_root, "secrets")).FullName,
            ["MARSHAL__Http__Urls"] = "http://127.0.0.1:0",
            ["MARSHAL__Models__Fast__Tier"] = "fast",
            ["MARSHAL__Models__Fast__Provider"] = "ollama",
            ["MARSHAL__Models__Fast__Model"] = "qwen3",
            ["MARSHAL__Models__Standard__Tier"] = "standard",
            ["MARSHAL__Models__Standard__Provider"] = "ollama",
            ["MARSHAL__Models__Standard__Model"] = "qwen3",
            ["MARSHAL__Embedding__Provider"] = "ollama",
            ["MARSHAL__Embedding__Model"] = "bge-m3",
            ["MARSHAL__Instance__TimeZone"] = "Europe/Vienna",
        };
    }
}
