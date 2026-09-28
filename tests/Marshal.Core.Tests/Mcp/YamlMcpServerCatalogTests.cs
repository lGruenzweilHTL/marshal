using Marshal.Abstractions;
using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Mcp;
using Marshal.Core.Configuration;
using Marshal.Core.Mcp;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Core.Tests.Mcp;

public sealed class YamlMcpServerCatalogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-mcp-").FullName;
    private readonly FakeSecretStore _secrets = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Load_HttpAndStdioServers_ParsedWithNamespaces()
    {
        Write("ha.yaml", """
            id: ha
            transport: http
            url: http://homeassistant:8123/api/mcp
            authTokenSecret: ha_token
            """);
        Write("search.yaml", """
            id: websearch
            transport: stdio
            command: npx
            args: ["-y", "some-search-server"]
            toolPrefix: search
            untrusted: true
            envSecrets:
              - variable: SEARCH_API_KEY
                secret: search_key
            """);

        using YamlMcpServerCatalog catalog = Load();

        Assert.Empty(catalog.Errors);
        McpServerDefinition ha = catalog.Servers.Single(server => server.Id == "ha");
        Assert.Equal((McpTransportKind.Http, "ha", "http://homeassistant:8123/api/mcp", "ha_token", false),
            (ha.Transport, ha.Namespace, ha.Url?.ToString(), ha.AuthTokenSecret?.Value, ha.Untrusted));
        McpServerDefinition search = catalog.Servers.Single(server => server.Id == "websearch");
        Assert.Equal((McpTransportKind.Stdio, "search", "npx", true), (search.Transport, search.Namespace, search.Command, search.Untrusted));
        Assert.Equal(["-y", "some-search-server"], search.Arguments);
        Assert.Equal("search_key", search.EnvironmentSecrets["SEARCH_API_KEY"].Value);
    }

    [Theory]
    [InlineData("id: home_assistant\ntransport: http\nurl: http://ha/api/mcp\n", "home_assistant")]
    [InlineData("id: ha\ntoolPrefix: Home\ntransport: http\nurl: http://ha/api/mcp\n", "Home")]
    [InlineData("transport: http\nurl: http://ha/api/mcp\n", "id")]
    [InlineData("id: ha\ntransport: http\n", "url")]
    [InlineData("id: ha\ntransport: http\nurl: /api/mcp\n", "url")]
    [InlineData("id: ha\ntransport: stdio\n", "command")]
    [InlineData("id: ha\ntransport: sse\nurl: http://ha/api/mcp\n", "transport")]
    [InlineData("id: ha\ntransport: http\nurl: http://ha/api/mcp\nauthTokenSecret: Bad Name\n", "authTokenSecret")]
    [InlineData("id: ha\ntransport: http\nurl: http://ha/api/mcp\nunknownKey: 1\n", "unknownKey")]
    public void Load_InvalidServer_RejectedWithErrorOthersLoad(string yaml, string named)
    {
        Write("bad.yaml", yaml);
        Write("good.yaml", "id: good\ntransport: http\nurl: http://good/mcp\n");

        using YamlMcpServerCatalog catalog = Load();

        Assert.Equal(["good"], catalog.Servers.Select(server => server.Id));
        ConfigFileError error = Assert.Single(catalog.Errors);
        Assert.Equal("bad.yaml", error.File);
        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("id: mail\ntransport: http\nurl: http://m/mcp\n")]
    [InlineData("id: other\ntoolPrefix: timers\ntransport: http\nurl: http://m/mcp\n")]
    public void Load_ReservedNamespace_Rejected(string yaml)
    {
        Write("reserved.yaml", yaml);

        using YamlMcpServerCatalog catalog = Load();

        Assert.Empty(catalog.Servers);
        Assert.Contains("reserved", Assert.Single(catalog.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_SameNamespaceTwice_LaterFileRejected()
    {
        Write("a.yaml", "id: ha\ntransport: http\nurl: http://a/mcp\n");
        Write("b.yaml", "id: home\ntoolPrefix: ha\ntransport: http\nurl: http://b/mcp\n");

        using YamlMcpServerCatalog catalog = Load();

        Assert.Equal(["ha"], catalog.Servers.Select(server => server.Id));
        Assert.Equal("b.yaml", Assert.Single(catalog.Errors).File);
    }

    [Fact]
    public void Load_LiteralSecret_RejectedWithoutValue()
    {
        Write("leak.yaml", "id: leak\ntransport: http\nurl: http://l/mcp\ntoken: hunter2\n");

        using YamlMcpServerCatalog catalog = Load();

        Assert.Empty(catalog.Servers);
        ConfigFileError error = Assert.Single(catalog.Errors);
        Assert.Contains("token", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingDirectory_NoServers()
    {
        Directory.Delete(_directory, recursive: true);

        using YamlMcpServerCatalog catalog = Load();

        Assert.Empty(catalog.Servers);
        Assert.Empty(catalog.Errors);
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task Resolve_AuthTokenAndEnvironmentFromSecretStore()
    {
        Write("s.yaml", "id: s\ntransport: stdio\ncommand: srv\nauthTokenSecret: s_token\nenvSecrets:\n  - variable: API_KEY\n    secret: s_key\n");
        _secrets.Values["s_token"] = "tok";
        _secrets.Values["s_key"] = "key";
        using YamlMcpServerCatalog catalog = Load();

        Result<McpConnectionSettings> settings = await catalog.ResolveAsync(catalog.Servers[0], Ct);

        Assert.True(settings.IsSuccess);
        Assert.Equal("Bearer tok", settings.Value.Headers["Authorization"]);
        Assert.Equal("key", settings.Value.Environment["API_KEY"]);
    }

    [Fact]
    public async Task Resolve_UnresolvedSecret_FailsNamingReference()
    {
        Write("s.yaml", "id: s\ntransport: http\nurl: http://s/mcp\nauthTokenSecret: s_token\n");
        using YamlMcpServerCatalog catalog = Load();

        Result<McpConnectionSettings> settings = await catalog.ResolveAsync(catalog.Servers[0], Ct);

        Assert.True(settings.IsFailure);
        Assert.Contains("s.yaml: authTokenSecret (s_token)", settings.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reload_AddedChangedRemovedFiles_NotifiesAndUpdates()
    {
        Write("a.yaml", "id: a\ntransport: http\nurl: http://a/mcp\n");
        using YamlMcpServerCatalog catalog = Load();
        int notified = 0;
        using IDisposable subscription = catalog.OnChange(() => notified++);

        Write("b.yaml", "id: b\ntransport: http\nurl: http://b/mcp\n");
        File.Delete(Path.Combine(_directory, "a.yaml"));
        catalog.Reload();

        Assert.Equal(["b"], catalog.Servers.Select(server => server.Id));
        Assert.Equal(1, notified);
    }

    [Fact]
    public async Task FileAddedOnDisk_ReloadsWithoutRestart()
    {
        using YamlMcpServerCatalog catalog = Load(watch: true);

        Write("late.yaml", "id: late\ntransport: http\nurl: http://late/mcp\n");

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (catalog.Servers.Count == 0 && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(50, Ct);

        Assert.Equal(["late"], catalog.Servers.Select(server => server.Id));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private YamlMcpServerCatalog Load(bool watch = false) =>
        new(_directory, new ConfigSecretScanner(), _secrets, NullLogger<YamlMcpServerCatalog>.Instance, watch);

    private void Write(string file, string yaml) => File.WriteAllText(Path.Combine(_directory, file), yaml);
}
