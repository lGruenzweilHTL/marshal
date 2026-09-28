using Marshal.Abstractions.Secrets;
using Marshal.Core.Mcp;
using Marshal.Testing;

namespace Marshal.Core.Tests.Mcp;

public sealed class McpSecretReferencesTests
{
    [Fact]
    public async Task List_AuthTokenAndEnvironmentSecrets_WithFileLocation()
    {
        var catalog = new FakeMcpServerCatalog();
        catalog.Definitions.Add(FakeMcpServerCatalog.Server("ha") with { AuthTokenSecret = SecretName.Create("ha_token").Value, File = "ha.yaml" });
        catalog.Definitions.Add(FakeMcpServerCatalog.Server("search") with
        {
            EnvironmentSecrets = new Dictionary<string, SecretName> { ["SEARCH_API_KEY"] = SecretName.Create("search_key").Value },
            File = "search.yaml",
        });

        IReadOnlyList<SecretReference> references = await new McpSecretReferences(catalog).ListAsync(TestExecutionContext.OwnerA, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                new SecretReference(SecretName.Create("ha_token").Value, "mcp/ha.yaml: authTokenSecret", OwnerScoped: false),
                new SecretReference(SecretName.Create("search_key").Value, "mcp/search.yaml: envSecrets (SEARCH_API_KEY)", OwnerScoped: false),
            ],
            references);
    }
}
