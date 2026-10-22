namespace Marshal.Integrations.Moonraker.Tests;

public sealed class PrinterOptionsValidatorTests
{
    [Fact]
    public void PowerEntity_Switch_Valid()
    {
        var options = new PrinterOptions { Printers = [new PrinterConfig { Name = "voron", PowerEntity = "switch.voron_power" }, new PrinterConfig { Name = "mini" }] };

        Assert.True(new PrinterOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("light.voron")]
    [InlineData("switch.")]
    [InlineData("voron_power")]
    public void PowerEntity_OtherDomain_FailsNamingTheKey(string entity)
    {
        var options = new PrinterOptions { Printers = [new PrinterConfig { Name = "voron", PowerEntity = entity }] };

        Microsoft.Extensions.Options.ValidateOptionsResult result = new PrinterOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("printers[0].powerEntity", result.FailureMessage);
    }
}
