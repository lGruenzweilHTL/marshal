using Marshal.Abstractions.Secrets;
using Marshal.Core.Secrets;
using Marshal.Testing;
using Microsoft.Extensions.Configuration;

namespace Marshal.Core.Tests.Secrets;

public sealed class ConfigurationSecretReferencesTests
{
    [Fact]
    public async Task List_KeysEndingInSecret_WithTheirYamlPath()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["models:claude:apiKeySecret"] = "anthropic_key",
            ["auth:providers:0:clientCredentialSecret"] = "authentik_client",
            ["models:claude:model"] = "claude",
        }).Build();

        IReadOnlyList<SecretReference> references = await new ConfigurationSecretReferences(configuration).ListAsync(TestExecutionContext.OwnerA, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("authentik_client", "marshal.yaml: auth.providers[0].clientCredentialSecret"), ("anthropic_key", "marshal.yaml: models.claude.apiKeySecret")],
            references.Select(reference => (reference.Name.Value, reference.Location)));
        Assert.All(references, reference => Assert.False(reference.OwnerScoped));
    }

    [Fact]
    public async Task List_EmptyOrInvalidNames_AreIgnored()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["a:apiKeySecret"] = "",
            ["b:apiKeySecret"] = "Not Valid",
        }).Build();

        Assert.Empty(await new ConfigurationSecretReferences(configuration).ListAsync(TestExecutionContext.OwnerA, TestContext.Current.CancellationToken));
    }
}
