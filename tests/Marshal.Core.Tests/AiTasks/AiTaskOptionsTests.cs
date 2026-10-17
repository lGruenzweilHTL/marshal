using Marshal.Core.AiTasks;
using Marshal.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.AiTasks;

public sealed class AiTaskOptionsTests
{
    [Fact]
    public void Defaults_SixtySecondsFourAttachmentsTenMegabytes()
    {
        AiTaskOptions options = Bind([]);

        Assert.Equal(60, options.TimeoutSeconds);
        Assert.Equal(4, options.MaxAttachments);
        Assert.Equal(10_485_760, options.MaxAttachmentBytes);
    }

    [Theory]
    [InlineData("aiTasks:timeoutSeconds", "0", "aiTasks.timeoutSeconds")]
    [InlineData("aiTasks:timeoutSeconds", "-1", "aiTasks.timeoutSeconds")]
    [InlineData("aiTasks:maxAttachments", "0", "aiTasks.maxAttachments")]
    [InlineData("aiTasks:maxAttachmentBytes", "0", "aiTasks.maxAttachmentBytes")]
    [InlineData("aiTasks:maxAttachmentBytes", "26214401", "aiTasks.maxAttachmentBytes")]
    public void OutOfRange_FailsNamingKey(string key, string value, string name)
    {
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => Bind(new() { [key] = value }));

        Assert.Contains(error.Failures, failure => failure.StartsWith(name, StringComparison.Ordinal));
    }

    [Fact]
    public void MaxAttachmentBytes_TwentyFiveMegabytes_Allowed()
    {
        Assert.Equal(26_214_400, Bind(new() { ["aiTasks:maxAttachmentBytes"] = "26214400" }).MaxAttachmentBytes);
    }

    private static AiTaskOptions Bind(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddOptions<AiTaskOptions>().Bind(configuration.GetSection(AiTaskOptions.Section)).ValidateAttributes();
        return services.BuildServiceProvider().GetRequiredService<IOptions<AiTaskOptions>>().Value;
    }
}
