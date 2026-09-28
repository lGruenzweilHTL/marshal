using Marshal.Core.Guard;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Guard;

public sealed class ConfiguredConfirmationKeywordsTests
{
    private readonly ConfiguredConfirmationKeywords _keywords = new(Options.Create(new ConfirmationOptions()));

    [Theory]
    [InlineData("Bestätigt!", "de")]
    [InlineData("  bestätigen ", "de")]
    [InlineData("Ja, mach!", "de")]
    [InlineData("CONFIRM.", "en")]
    [InlineData("Yes, do it!", "en")]
    [InlineData("approved", "en")]
    [InlineData("confirmed", "en-US")]
    public void IsConfirmation_NormalizedFullUtterance_Matches(string utterance, string language)
    {
        Assert.True(_keywords.IsConfirmation(utterance, language));
    }

    [Theory]
    [InlineData("bestätigt, aber erst morgen", "de")]
    [InlineData("please confirm the order", "en")]
    [InlineData("", "en")]
    [InlineData("bestätigt", "en")]
    [InlineData("confirm", "de")]
    [InlineData("confirm", "fr")]
    [InlineData("confirm", "e")]
    public void IsConfirmation_NoExactMatch_False(string utterance, string language)
    {
        Assert.False(_keywords.IsConfirmation(utterance, language));
    }

    [Fact]
    public void IsConfirmation_ConfiguredKeywords_ReplaceDefaults()
    {
        var options = new ConfirmationOptions { Keywords = new() { ["en"] = ["Make it so!"] } };
        var keywords = new ConfiguredConfirmationKeywords(Options.Create(options));

        Assert.True(keywords.IsConfirmation("make it so", "en"));
        Assert.False(keywords.IsConfirmation("confirm", "en"));
    }

    [Theory]
    [InlineData("  Bestätigt!!  ", "bestätigt")]
    [InlineData("Yes,   do\tit.", "yes do it")]
    [InlineData("¿Sí?", "sí")]
    public void Normalize_TrimsLowercasesStripsPunctuation(string input, string expected)
    {
        Assert.Equal(expected, ConfiguredConfirmationKeywords.Normalize(input));
    }
}
