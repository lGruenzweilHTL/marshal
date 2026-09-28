using Marshal.Api.Tests.Http;

namespace Marshal.Api.Tests.Auth;

/// <summary>
/// Login provider configuration for API tests.
/// </summary>
internal static class AuthFactory
{
    public const string ClientSecret = "oidc-client-secret";

    public const string Authority = "https://idp.test/";

    public static ProviderSettings Oidc(string name, string displayName) => new(new Dictionary<string, string?>
    {
        ["name"] = name,
        ["type"] = "oidc",
        ["displayName"] = displayName,
        ["authority"] = Authority,
        ["clientId"] = "marshal",
        ["clientCredentialSecret"] = "authentik_client",
        ["roleMappings:marshal-admins"] = "admin",
        ["roleMappings:marshal-users"] = "user",
    });

    public static ProviderSettings Local() => new(new Dictionary<string, string?> { ["name"] = "local", ["type"] = "local" });

    /// <summary>A host with the providers configured and the OIDC client secret in the secrets directory.</summary>
    public static async Task<MarshalApiFactory> CreateAsync(params ProviderSettings[] providers)
    {
        var settings = new Dictionary<string, string?>();
        for (int index = 0; index < providers.Length; index++)
        {
            foreach ((string key, string? value) in providers[index].ToSettings(index))
                settings[key] = value;
        }

        var factory = new MarshalApiFactory(settings);
        await WriteClientSecretAsync(factory);
        return factory;
    }

    public static Task WriteClientSecretAsync(MarshalApiFactory factory) =>
        File.WriteAllTextAsync(Path.Combine(factory.SecretsDirectory, "authentik_client"), ClientSecret);

    public sealed record ProviderSettings(Dictionary<string, string?> Values)
    {
        public Dictionary<string, string?> ToSettings(int index) =>
            Values.ToDictionary(pair => $"auth:providers:{index}:{pair.Key}", pair => pair.Value);
    }
}
