using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Artifacts.Tests;

public sealed class ArtifactOptionsTests
{
    [Fact]
    public void Defaults_WithoutASection()
    {
        ArtifactOptions options = Options(new Dictionary<string, string?>());

        Assert.Equal((5120, 200, 15), (options.QuotaMb, options.MaxUploadMb, options.SignedLinkMinutes));
    }

    [Theory]
    [InlineData("artifacts:quotaMb", "0", "artifacts.quotaMb")]
    [InlineData("artifacts:maxUploadMb", "0", "artifacts.maxUploadMb")]
    [InlineData("artifacts:signedLinkMinutes", "0", "artifacts.signedLinkMinutes")]
    [InlineData("artifacts:signedLinkMinutes", "1441", "artifacts.signedLinkMinutes")]
    public void InvalidValue_FailsNamingTheSetting(string key, string value, string message)
    {
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => Options(new Dictionary<string, string?> { [key] = value }));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    private static ArtifactOptions Options(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using ServiceProvider services = new ServiceCollection().AddMarshalArtifacts(configuration, _ => Path.GetTempPath()).BuildServiceProvider();
        return services.GetRequiredService<IOptions<ArtifactOptions>>().Value;
    }
}
