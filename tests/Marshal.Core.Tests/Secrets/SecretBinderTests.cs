using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;
using Marshal.Core.Secrets;

namespace Marshal.Core.Tests.Secrets;

public sealed class SecretBinderTests
{
    [Fact]
    public async Task Bind_ResolvesSecretIntoMarkedProperty()
    {
        var options = new RootOptions { Models = { ["claude"] = new ModelEntry { ApiKeySecret = "anthropic_key" } } };

        SecretBindingReport report = await Binder(("anthropic_key", "sk-value")).BindAsync(options, "", TestContext.Current.CancellationToken);

        Assert.True(report.IsValid);
        Assert.Equal("sk-value", options.Models["claude"].ApiKey);
    }

    [Fact]
    public async Task Bind_RequiredSecretMissing_ReportsReferenceNotValue()
    {
        var options = new RootOptions { Models = { ["claude"] = new ModelEntry { ApiKeySecret = "anthropic_key" } } };

        SecretBindingReport report = await Binder().BindAsync(options, "", TestContext.Current.CancellationToken);

        Assert.False(report.IsValid);
        Assert.Equal(["models.claude.apiKeySecret (anthropic_key)"], report.MissingRequired);
    }

    [Fact]
    public async Task Bind_OptionalSecretMissing_ProducesWarningOnly()
    {
        var options = new RootOptions { Printers = [new PrinterEntry { ApiKeySecret = "moonraker_voron" }] };

        SecretBindingReport report = await Binder().BindAsync(options, "", TestContext.Current.CancellationToken);

        Assert.True(report.IsValid);
        Assert.Equal(["printers[0].apiKeySecret (moonraker_voron)"], report.MissingOptional);
    }

    [Fact]
    public async Task Bind_ValueBoundFromConfiguration_IsRejected()
    {
        var options = new RootOptions { Models = { ["claude"] = new ModelEntry { ApiKey = "literal" } } };

        SecretBindingReport report = await Binder().BindAsync(options, "", TestContext.Current.CancellationToken);

        Assert.False(report.IsValid);
        Assert.Equal(["models.claude.apiKey"], report.BoundFromConfiguration);
        Assert.Null(options.Models["claude"].ApiKey);
    }

    [Fact]
    public async Task Bind_NoReference_LeavesPropertyEmpty()
    {
        var options = new RootOptions { Models = { ["local"] = new ModelEntry() } };

        SecretBindingReport report = await Binder().BindAsync(options, "", TestContext.Current.CancellationToken);

        Assert.True(report.IsValid);
        Assert.Empty(report.MissingOptional);
        Assert.Null(options.Models["local"].ApiKey);
    }

    [Fact]
    public async Task Bind_InvalidReferenceName_IsMissingRequired()
    {
        var options = new RootOptions { Models = { ["claude"] = new ModelEntry { ApiKeySecret = "Not-A-Name" } } };

        SecretBindingReport report = await Binder().BindAsync(options, "", TestContext.Current.CancellationToken);

        Assert.Equal(["models.claude.apiKeySecret (Not-A-Name)"], report.MissingRequired);
    }

    private static SecretBinder Binder(params (string Name, string Value)[] secrets) => new(new DictionarySecretStore(secrets.ToDictionary(s => s.Name, s => s.Value)));

    public sealed class RootOptions
    {
        public Dictionary<string, ModelEntry> Models { get; set; } = [];

        public List<PrinterEntry> Printers { get; set; } = [];
    }

    public sealed class ModelEntry
    {
        public string? ApiKeySecret { get; set; }

        [Secret(nameof(ApiKeySecret))]
        public string? ApiKey { get; set; }
    }

    public sealed class PrinterEntry
    {
        public string? ApiKeySecret { get; set; }

        [Secret(nameof(ApiKeySecret), Required = false)]
        public string? ApiKey { get; set; }
    }

    private sealed class DictionarySecretStore(Dictionary<string, string> values) : ISecretStore
    {
        public Task<Result<string>> GetAsync(SecretName name, CancellationToken cancellationToken) =>
            Task.FromResult(values.TryGetValue(name.Value, out string? value)
                ? Result.Success(value)
                : Result.Failure<string>(new Error("secret.not_found", "missing", ErrorType.NotFound)));

        public Task<Result<string>> GetForOwnerAsync(SecretName name, OwnerId owner, CancellationToken cancellationToken) =>
            GetAsync(name, cancellationToken);
    }
}
