using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Memory.Tests;

public sealed class MemoryOptionsTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    public void DuplicateThreshold_OutOfRange_FailsNamingTheKey(string value)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["memory:duplicateThreshold"] = value }).Build();
        using ServiceProvider provider = new ServiceCollection().AddMarshalMemory(configuration).BuildServiceProvider();

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MemoryOptions>>().Value);

        Assert.Contains("memory.duplicateThreshold", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateThreshold_Default()
    {
        Assert.Equal(0.92, new MemoryOptions().DuplicateThreshold);
    }
}
