using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Proxmox.Tests;

public sealed class InfraToolsTests : IAsyncLifetime
{
    private FakeProxmox _pve = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _pve = await FakeProxmox.StartAsync();

    public async ValueTask DisposeAsync() => await _pve.DisposeAsync();

    [Fact]
    public async Task Tools_SafeTrustedInInfraNamespace()
    {
        IReadOnlyList<ITool> tools = await Source().GetToolsAsync(Ct);

        Assert.Equal(["infra.overview", "infra.guests", "infra.storage", "infra.tasks"], tools.Select(tool => tool.Descriptor.Id.ToString()));
        Assert.All(tools, tool => Assert.Equal((RiskTier.Safe, false), (tool.Descriptor.DefaultTier, tool.Descriptor.UntrustedOutput)));
    }

    [Fact]
    public async Task NotConfigured_NoTools()
    {
        var source = new InfraToolSource(Client(), Options.Create(new ProxmoxOptions()));

        Assert.Empty(await source.GetToolsAsync(Ct));
    }

    [Fact]
    public async Task Overview_NodesGuestCountsAndFullStorages()
    {
        JsonElement overview = await InvokeAsync("overview", new { });

        JsonElement pve1 = overview.GetProperty("nodes")[0];
        Assert.Equal(("pve1", true, 12.5, 25.0, 100.0), (pve1.GetProperty("name").GetString(), pve1.GetProperty("online").GetBoolean(), pve1.GetProperty("cpuPercent").GetDouble(), pve1.GetProperty("memoryPercent").GetDouble(), pve1.GetProperty("uptimeHours").GetDouble()));
        Assert.Equal((2, 1), (overview.GetProperty("guests").GetProperty("running").GetInt32(), overview.GetProperty("guests").GetProperty("stopped").GetInt32()));
        JsonElement full = Assert.Single(overview.GetProperty("fullStorages").EnumerateArray());
        Assert.Equal(("local", "pve1", 90.0), (full.GetProperty("name").GetString(), full.GetProperty("node").GetString(), full.GetProperty("percentUsed").GetDouble()));
    }

    [Fact]
    public async Task Guests_FilteredByStatusAndNode_TemplatesExcluded()
    {
        JsonElement running = await InvokeAsync("guests", new { status = "running" });
        JsonElement onPve2 = await InvokeAsync("guests", new { node = "PVE2" });
        JsonElement all = await InvokeAsync("guests", new { });

        Assert.Equal(["nas", "pihole"], running.EnumerateArray().Select(guest => guest.GetProperty("name").GetString()));
        Assert.Equal(("vm", "container"), (running[0].GetProperty("type").GetString(), running[1].GetProperty("type").GetString()));
        Assert.Equal(4.0, running[0].GetProperty("memoryGb").GetDouble());
        Assert.Equal("win11", Assert.Single(onPve2.EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(3, all.GetArrayLength());
    }

    [Fact]
    public async Task Guests_UnknownStatus_Invalid()
    {
        Result<ToolOutput> result = await (await ToolAsync("guests")).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(new { status = "zombie" }), TestExecutionContext.Text()), Ct);

        Assert.Equal("infra.invalid_arguments", result.Error.Code);
    }

    [Fact]
    public async Task Storage_SharedStorageOnce()
    {
        JsonElement storage = await InvokeAsync("storage", new { });

        Assert.Equal(2, storage.GetArrayLength());
        JsonElement nfs = storage.EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == "nfs-backup");
        Assert.Equal((JsonValueKind.Null, true, 25.0), (nfs.GetProperty("node").ValueKind, nfs.GetProperty("shared").GetBoolean(), nfs.GetProperty("percentUsed").GetDouble()));
    }

    [Fact]
    public async Task Tasks_NewestFirst_FailedOnlyAndSince()
    {
        JsonElement all = await InvokeAsync("tasks", new { });
        JsonElement failed = await InvokeAsync("tasks", new { failedOnly = true });
        JsonElement recent = await InvokeAsync("tasks", new { since = DateTimeOffset.FromUnixTimeSeconds(1760005000).ToString("O") });

        Assert.Equal(["vzdump", "qmstart", "vzdump"], all.EnumerateArray().Select(task => task.GetProperty("type").GetString()));
        Assert.Equal("running", all[0].GetProperty("status").GetString());
        JsonElement backup = Assert.Single(failed.EnumerateArray());
        Assert.Equal(("vzdump", "job errors", "pve1"), (backup.GetProperty("type").GetString(), backup.GetProperty("status").GetString(), backup.GetProperty("node").GetString()));
        Assert.Equal(2, recent.GetArrayLength());
    }

    [Fact]
    public async Task Tasks_LimitedToFifty()
    {
        _pve.Tasks = [.. Enumerable.Range(0, 60).Select(i => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
        {
            ["upid"] = $"UPID:{i}", ["type"] = "qmstart", ["id"] = "100", ["node"] = "pve1", ["user"] = "root@pam", ["starttime"] = 1760000000 + i, ["endtime"] = 1760000001 + i, ["status"] = "OK",
        })];

        Assert.Equal(InfraToolSource.TaskLimit, (await InvokeAsync("tasks", new { })).GetArrayLength());
    }

    [Fact]
    public async Task ClientFailure_ReturnedAsToolError()
    {
        var secrets = new FakeSecretStore();
        var source = new InfraToolSource(new ProxmoxRestClient(new Http(_pve), Configured(), secrets), Configured());
        ITool overview = (await source.GetToolsAsync(Ct))[0];

        Result<ToolOutput> result = await overview.InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(new { }), TestExecutionContext.Text()), Ct);

        Assert.Equal("infra.unavailable", result.Error.Code);
    }

    private async Task<JsonElement> InvokeAsync(string name, object arguments)
    {
        Result<ToolOutput> result = await (await ToolAsync(name)).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), TestExecutionContext.Text()), Ct);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return JsonDocument.Parse(result.Value.Content).RootElement.Clone();
    }

    private async Task<ITool> ToolAsync(string name) => (await Source().GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == name);

    private InfraToolSource Source() => new(Client(), Configured());

    private ProxmoxRestClient Client()
    {
        var secrets = new FakeSecretStore();
        secrets.Values["proxmox_token"] = FakeProxmox.TokenValue;
        return new ProxmoxRestClient(new Http(_pve), Configured(), secrets);
    }

    private static IOptions<ProxmoxOptions> Configured() =>
        Options.Create(new ProxmoxOptions { Url = new Uri("https://pve.lan:8006/"), TokenId = FakeProxmox.TokenId, TokenSecret = "proxmox_token" });

    private sealed class Http(FakeProxmox pve) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(pve.Server.CreateHandler());
    }
}
