using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Marshal.Abstractions;
using Marshal.Testing;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Proxmox.Tests;

public sealed class ProxmoxRestClientTests : IAsyncLifetime
{
    private FakeProxmox _pve = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _pve = await FakeProxmox.StartAsync();

    public async ValueTask DisposeAsync() => await _pve.DisposeAsync();

    [Fact]
    public async Task Resources_ParsesNodesGuestsAndStorages_WithTokenHeader()
    {
        Result<ProxmoxResources> resources = await Client().GetResourcesAsync(Ct);

        Assert.True(resources.IsSuccess, resources.IsFailure ? resources.Error.Message : null);
        Assert.Equal(new ProxmoxNode("pve1", true, 0.125, 8589934592, 34359738368, 360000), resources.Value.Nodes[0]);
        Assert.False(resources.Value.Nodes[1].Online);
        Assert.Equal(new ProxmoxGuest(101, "pihole", "lxc", "pve1", "running", 0.01, 268435456, 536870912, 3600, false), resources.Value.Guests[1]);
        Assert.True(resources.Value.Guests.Single(guest => guest.VmId == 9000).Template);
        Assert.Equal(new ProxmoxStorage("nfs-backup", "pve1", "nfs", 500000000000, 2000000000000, true), resources.Value.Storages[1]);
        (string method, string path, string? authorization) = Assert.Single(_pve.Requests);
        Assert.Equal(("GET", "/api2/json/cluster/resources", $"PVEAPIToken={FakeProxmox.TokenId}={FakeProxmox.TokenValue}"), (method, path, authorization));
    }

    [Fact]
    public async Task Tasks_ParsesRunningAndFinished()
    {
        Result<IReadOnlyList<ProxmoxTask>> tasks = await Client().GetTasksAsync(Ct);

        Assert.Equal(3, tasks.Value.Count);
        Assert.Equal(("vzdump", null, "job errors"), (tasks.Value[0].Type, tasks.Value[0].GuestId, tasks.Value[0].Status));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1760003600), tasks.Value[0].End);
        Assert.Equal(("101", null, null), (tasks.Value[2].GuestId, tasks.Value[2].End, tasks.Value[2].Status));
    }

    [Fact]
    public async Task WrongToken_UnavailableNamingTheToken()
    {
        var secrets = new FakeSecretStore();
        secrets.Values["proxmox_token"] = "wrong";

        Result<ProxmoxResources> resources = await Client(secrets).GetResourcesAsync(Ct);

        Assert.Equal("infra.unavailable", resources.Error.Code);
        Assert.Contains("rejected the API token", resources.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSecret_UnavailableNamingTheSecret()
    {
        Result<ProxmoxResources> resources = await Client(new FakeSecretStore()).GetResourcesAsync(Ct);

        Assert.Equal("infra.unavailable", resources.Error.Code);
        Assert.Contains("proxmox_token", resources.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_pve.Requests);
    }

    [Fact]
    public async Task Unreachable_Unavailable()
    {
        var client = new ProxmoxRestClient(new Http(new HttpClient(new Refusing())), Options("https://pve.lan:8006/"), Secrets());

        Assert.Equal("infra.unavailable", (await client.GetResourcesAsync(Ct)).Error.Code);
    }

    [Fact]
    public async Task PinnedFingerprint_AcceptsOnlyThatCertificate()
    {
        using X509Certificate2 certificate = SelfSigned();
        await using FakeProxmox tls = await FakeProxmox.StartAsync(certificate);
        string fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        string other = new('0', 64);

        Result<ProxmoxResources> pinned = await TlsClient(tls, fingerprint).GetResourcesAsync(Ct);
        Result<ProxmoxResources> mismatch = await TlsClient(tls, other).GetResourcesAsync(Ct);
        Result<ProxmoxResources> unpinned = await TlsClient(tls, null).GetResourcesAsync(Ct);

        Assert.True(pinned.IsSuccess, pinned.IsFailure ? pinned.Error.Message : null);
        Assert.Equal(("infra.unavailable", "infra.unavailable"), (mismatch.Error.Code, unpinned.Error.Code));
        Assert.Single(tls.Requests);
    }

    [Fact]
    public void PinnedFingerprint_NormalizesColonsAndCase()
    {
        string hex = new string('a', 62) + "0f";
        var options = new ProxmoxOptions { CertificateFingerprint = string.Join(':', Enumerable.Range(0, 32).Select(i => hex.Substring(i * 2, 2))) };

        Assert.Equal(hex.ToUpperInvariant(), options.PinnedFingerprint);
        Assert.Null(new ProxmoxOptions { CertificateFingerprint = "abc" }.PinnedFingerprint);
    }

    private ProxmoxRestClient Client(FakeSecretStore? secrets = null) =>
        new(new Http(new HttpClient(_pve.Server.CreateHandler())), Options("https://pve.lan:8006/"), secrets ?? Secrets());

    private static ProxmoxRestClient TlsClient(FakeProxmox tls, string? fingerprint)
    {
        IOptions<ProxmoxOptions> options = Options(tls.Address.ToString(), fingerprint);
        return new ProxmoxRestClient(new Http(new HttpClient(ProxmoxServiceCollectionExtensions.Handler(options.Value.PinnedFingerprint))), options, Secrets());
    }

    private static IOptions<ProxmoxOptions> Options(string url, string? fingerprint = null) => Microsoft.Extensions.Options.Options.Create(new ProxmoxOptions
    {
        Url = new Uri(url),
        TokenId = FakeProxmox.TokenId,
        TokenSecret = "proxmox_token",
        CertificateFingerprint = fingerprint,
    });

    private static FakeSecretStore Secrets()
    {
        var secrets = new FakeSecretStore();
        secrets.Values["proxmox_token"] = FakeProxmox.TokenValue + "\n";
        return secrets;
    }

    private static X509Certificate2 SelfSigned()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=pve.lan", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null);
    }

    private sealed class Http(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused");
    }
}
