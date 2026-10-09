using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.GitHub.Tests;

public sealed class GitHubOptionsTests
{
    [Fact]
    public void Valid_Succeeds() => Assert.Null(Failure(new() { ["github:tokenSecret"] = "github_pat", ["github:repos:marshal"] = "lukas/marshal" }));

    [Fact]
    public void NotConfigured_Succeeds() => Assert.Null(Failure([]));

    [Theory]
    [InlineData("github:repos:Marshal", "lukas/marshal", "github.repos ")]
    [InlineData("github:repos:marshal", "https://github.com/lukas/marshal", "github.repos ")]
    [InlineData("github:repos:marshal", "marshal", "github.repos ")]
    public void InvalidRepo_FailsNamingTheKey(string key, string value, string expected)
    {
        Assert.StartsWith(expected, Failure(new() { ["github:tokenSecret"] = "github_pat", [key] = value }), StringComparison.Ordinal);
    }

    [Fact]
    public void ReposWithoutTokenSecret_Fails()
    {
        Assert.StartsWith("github.tokenSecret ", Failure(new() { ["github:repos:marshal"] = "lukas/marshal" }), StringComparison.Ordinal);
    }

    private static string? Failure(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using ServiceProvider services = new ServiceCollection().AddMarshalGitHub(configuration).BuildServiceProvider();
        try
        {
            _ = services.GetRequiredService<IOptions<GitHubOptions>>().Value;
            return null;
        }
        catch (OptionsValidationException failure)
        {
            return failure.Failures.First();
        }
    }
}
