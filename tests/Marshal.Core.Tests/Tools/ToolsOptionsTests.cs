using Marshal.Core.Agent;
using Marshal.Core.Configuration;
using Marshal.Core.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Tools;

public sealed class ToolsOptionsTests
{
    [Fact]
    public void Defaults_CoreNamespacesAndRetrievalValues()
    {
        ToolsOptions options = Bind<ToolsOptions>("tools", []);

        Assert.Equal(["agent", "memory", "tasks", "notifications", "timers", "places"], options.CoreNamespaces);
        Assert.True(options.Retrieval.Enabled);
        Assert.Equal(0.30, options.Retrieval.Threshold);
        Assert.Equal(3, options.Retrieval.MaxPerTurn);
        Assert.Equal(24, options.Retrieval.MinTools);
    }

    [Fact]
    public void Core_Configured_ReplacesDefaults()
    {
        ToolsOptions options = Bind<ToolsOptions>("tools", new() { ["tools:core:0"] = "agent", ["tools:core:1"] = "printer" });

        Assert.Equal(["agent", "printer"], options.CoreNamespaces);
    }

    [Theory]
    [InlineData("tools:retrieval:threshold", "1.5", "tools.retrieval.threshold")]
    [InlineData("tools:retrieval:threshold", "-0.1", "tools.retrieval.threshold")]
    [InlineData("tools:retrieval:maxPerTurn", "0", "tools.retrieval.maxPerTurn")]
    [InlineData("tools:retrieval:minTools", "-1", "tools.retrieval.minTools")]
    public void Retrieval_OutOfRange_FailsNamingKey(string key, string value, string name)
    {
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => Bind<ToolsOptions>("tools", new() { [key] = value }));

        Assert.Contains(error.Failures, failure => failure.StartsWith(name, StringComparison.Ordinal));
    }

    [Fact]
    public void FillerThreshold_DefaultsTo700()
    {
        VoiceOptions options = Bind<VoiceOptions>("voice", []);

        Assert.Equal(700, options.FillerThresholdMs);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void FillerThreshold_NotPositive_FailsNamingKey(string value)
    {
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => Bind<VoiceOptions>("voice", new() { ["voice:fillerThresholdMs"] = value }));

        Assert.Contains(error.Failures, failure => failure.StartsWith("voice.fillerThresholdMs", StringComparison.Ordinal));
    }

    private static T Bind<T>(string section, Dictionary<string, string?> values)
        where T : class
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddOptions<T>().Bind(configuration.GetSection(section)).ValidateAttributes();
        return services.BuildServiceProvider().GetRequiredService<IOptions<T>>().Value;
    }
}
