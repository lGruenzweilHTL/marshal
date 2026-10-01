using Marshal.Abstractions.Configuration;
using Marshal.Api.Tests.Http;

namespace Marshal.Api.Tests.Hosting;

public sealed class SecretStageTests
{
    [Fact]
    public void Startup_SeveralRequiredSecretsMissing_ReportsEveryReference()
    {
        using var factory = new MarshalApiFactory(new()
        {
            ["ha:url"] = "http://127.0.0.1:1",
            ["ha:tokenSecret"] = "ha_token",
            ["models:claude:tier"] = "advisor",
            ["models:claude:provider"] = "anthropic",
            ["models:claude:model"] = "claude-sonnet",
            ["models:claude:apiKeySecret"] = "anthropic_key",
        });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        IReadOnlyList<StartupError> errors = StartupErrors.Reported(error).Errors;
        Assert.Contains(new StartupError("marshal.yaml", "ha.tokenSecret", "(ha_token) does not resolve."), errors);
        Assert.Contains(new StartupError("marshal.yaml", "models.claude.apiKeySecret", "(anthropic_key) does not resolve."), errors);
    }
}
