using System.Collections;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Marshal.Api.Cli;
using Marshal.Api.Tests.Http;

namespace Marshal.Api.Tests.Cli;

public sealed class OpenApiExportTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-openapi-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void IsRequested_OnlyWithTheFlag()
    {
        Assert.True(OpenApiExport.IsRequested(["--export-openapi", "out.json"]));
        Assert.False(OpenApiExport.IsRequested(["tokens", "list"]));
        Assert.False(OpenApiExport.IsRequested([]));
    }

    [Fact]
    public async Task Export_WritesTheServedV1DocumentWithoutAServer()
    {
        string path = Path.Combine(_directory, "v1.json");
        var environment = new Hashtable { ["MARSHAL__Paths__Config"] = Path.Combine(_directory, "config"), ["MARSHAL__Paths__Data"] = Path.Combine(_directory, "data") };

        int exit = await OpenApiExport.RunAsync(["--export-openapi", path], environment);

        Assert.Equal(0, exit);
        await using var factory = new MarshalApiFactory();
        JsonNode served = (await factory.CreateClient().GetFromJsonAsync<JsonNode>("/openapi/v1.json", Ct))!;
        JsonNode exported = JsonNode.Parse(await File.ReadAllTextAsync(path, Ct))!;
        served.AsObject().Remove("servers");
        exported.AsObject().Remove("servers");
        Assert.True(JsonNode.DeepEquals(served, exported), "exported document differs from /openapi/v1.json");
        Assert.NotNull(exported["paths"]?["/api/v1/conversation/turns"]);
    }

    [Fact]
    public async Task Document_DescribesNumbersAsJsonNumbersOnly()
    {
        await using var factory = new MarshalApiFactory();

        JsonNode document = (await factory.CreateClient().GetFromJsonAsync<JsonNode>("/openapi/v1.json", Ct))!;

        JsonNode cost = document["components"]!["schemas"]!["RunSummary"]!["properties"]!["cost"]!;
        Assert.Equal("number", cost["type"]!.GetValue<string>());
        Assert.Null(cost["pattern"]);
    }

    [Theory]
    [InlineData("RunId")]
    [InlineData("SessionId")]
    [InlineData("OwnerId")]
    public async Task Document_DescribesGuidIdsAsUuidStrings(string schema)
    {
        await using var factory = new MarshalApiFactory();

        JsonNode document = (await factory.CreateClient().GetFromJsonAsync<JsonNode>("/openapi/v1.json", Ct))!;

        JsonNode id = document["components"]!["schemas"]![schema]!;
        Assert.Equal("string", id["type"]!.GetValue<string>());
        Assert.Equal("uuid", id["format"]!.GetValue<string>());
    }

    [Fact]
    public async Task CommittedSpaDocument_MatchesTheExport()
    {
        string root = RepositoryRoot();
        string path = Path.Combine(_directory, "v1.json");
        var environment = new Hashtable { ["MARSHAL__Paths__Config"] = Path.Combine(_directory, "config"), ["MARSHAL__Paths__Data"] = Path.Combine(_directory, "data") };

        await OpenApiExport.RunAsync(["--export-openapi", path], environment);

        JsonNode committed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "web", "openapi", "v1.json"), Ct))!;
        JsonNode exported = JsonNode.Parse(await File.ReadAllTextAsync(path, Ct))!;
        Assert.True(JsonNode.DeepEquals(committed, exported), "web/openapi/v1.json is stale: run `npm run gen:api` in web/ and commit the result.");
    }

    [Fact]
    public async Task Export_WithoutPath_FailsWithUsage()
    {
        using var error = new StringWriter();

        int exit = await OpenApiExport.RunAsync(["--export-openapi"], new Hashtable(), error);

        Assert.NotEqual(0, exit);
        Assert.Contains("--export-openapi <file>", error.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marshal.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
