using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.AspNetCore.TestHost;

namespace Marshal.Integrations.Proxmox.Tests;

/// <summary>
/// An in-process Proxmox VE API answering <c>/api2/json/cluster/resources</c> and <c>/cluster/tasks</c> in PVE's shape.
/// Either a <see cref="TestServer"/> or, for TLS tests, Kestrel on a loopback port with a self-signed certificate.
/// </summary>
internal sealed class FakeProxmox : IAsyncDisposable
{
    public const string TokenId = "marshal@pve!marshal";
    public const string TokenValue = "11111111-2222-3333-4444-555555555555";

    private readonly WebApplication _app;

    private FakeProxmox(WebApplication app) => _app = app;

    public ConcurrentQueue<(string Method, string Path, string? Authorization)> Requests { get; } = new();

    public JsonArray Resources { get; set; } = (JsonArray)JsonNode.Parse("""
        [
          {"id":"node/pve1","type":"node","node":"pve1","status":"online","cpu":0.125,"maxcpu":8,"mem":8589934592,"maxmem":34359738368,"uptime":360000},
          {"id":"node/pve2","type":"node","node":"pve2","status":"offline"},
          {"id":"qemu/100","type":"qemu","vmid":100,"name":"nas","node":"pve1","status":"running","cpu":0.5,"maxcpu":4,"mem":4294967296,"maxmem":8589934592,"uptime":7200,"template":0},
          {"id":"lxc/101","type":"lxc","vmid":101,"name":"pihole","node":"pve1","status":"running","cpu":0.01,"maxcpu":1,"mem":268435456,"maxmem":536870912,"uptime":3600},
          {"id":"qemu/102","type":"qemu","vmid":102,"name":"win11","node":"pve2","status":"stopped","cpu":0,"maxcpu":4,"mem":0,"maxmem":8589934592,"uptime":0,"template":0},
          {"id":"qemu/9000","type":"qemu","vmid":9000,"name":"debian-template","node":"pve1","status":"stopped","template":1},
          {"id":"storage/pve1/local","type":"storage","storage":"local","node":"pve1","plugintype":"dir","disk":90000000000,"maxdisk":100000000000,"shared":0,"status":"available"},
          {"id":"storage/pve1/nfs-backup","type":"storage","storage":"nfs-backup","node":"pve1","plugintype":"nfs","disk":500000000000,"maxdisk":2000000000000,"shared":1,"status":"available"},
          {"id":"storage/pve2/nfs-backup","type":"storage","storage":"nfs-backup","node":"pve2","plugintype":"nfs","disk":500000000000,"maxdisk":2000000000000,"shared":1,"status":"available"}
        ]
        """)!;

    public JsonArray Tasks { get; set; } = (JsonArray)JsonNode.Parse("""
        [
          {"upid":"UPID:pve1:1:1:6720A000:vzdump::root@pam:","type":"vzdump","id":"","node":"pve1","user":"root@pam","starttime":1760000000,"endtime":1760003600,"status":"job errors"},
          {"upid":"UPID:pve1:2:2:6720B000:qmstart:100:root@pam:","type":"qmstart","id":"100","node":"pve1","user":"root@pam","starttime":1760010000,"endtime":1760010005,"status":"OK"},
          {"upid":"UPID:pve1:3:3:6720C000:vzdump:101:root@pam:","type":"vzdump","id":"101","node":"pve1","user":"root@pam","starttime":1760020000}
        ]
        """)!;

    public TestServer Server => _app.GetTestServer();

    public Uri Address => new(_app.Urls.First());

    public static Task<FakeProxmox> StartAsync() => StartAsync(certificate: null);

    /// <summary>Kestrel over HTTPS with <paramref name="certificate"/>; otherwise a test server.</summary>
    public static async Task<FakeProxmox> StartAsync(X509Certificate2? certificate)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        if (certificate is null)
        {
            builder.WebHost.UseTestServer();
        }
        else
        {
            builder.WebHost.UseKestrelHttpsConfiguration();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.UseHttps(new HttpsConnectionAdapterOptions { ServerCertificate = certificate })));
        }

        WebApplication app = builder.Build();
        var fake = new FakeProxmox(app);
        app.Use(async (context, next) =>
        {
            fake.Requests.Enqueue((context.Request.Method, context.Request.Path, context.Request.Headers.Authorization.ToString()));
            if (context.Request.Headers.Authorization != $"PVEAPIToken={TokenId}={TokenValue}")
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });
        app.MapGet("/api2/json/cluster/resources", () => Results.Text(new JsonObject { ["data"] = fake.Resources.DeepClone() }.ToJsonString(), "application/json"));
        app.MapGet("/api2/json/cluster/tasks", () => Results.Text(new JsonObject { ["data"] = fake.Tasks.DeepClone() }.ToJsonString(), "application/json"));
        await app.StartAsync();
        return fake;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
