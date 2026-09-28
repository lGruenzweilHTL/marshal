using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Core.Guard;

namespace Marshal.Core.Tests.Guard;

public sealed class ConfirmationReasonTextTests
{
    [Theory]
    [InlineData("mail", "en", "your mail")]
    [InlineData("rss", "en", "RSS articles")]
    [InlineData("notes", "en", "your notes")]
    [InlineData("calendar", "en", "calendar events")]
    [InlineData("notifications", "en", "notifications")]
    [InlineData("web", "en", "web pages")]
    [InlineData("mcp:search", "en", "the tool server search")]
    [InlineData("custom", "en", "custom")]
    [InlineData("mail", "de", "deinen E-Mails")]
    [InlineData("rss", "de", "RSS-Artikeln")]
    [InlineData("notes", "de", "deinen Notizen")]
    [InlineData("calendar", "de", "Kalendereinträgen")]
    [InlineData("notifications", "de", "Benachrichtigungen")]
    [InlineData("web", "de", "Webseiten")]
    [InlineData("mcp:search", "de", "dem Tool-Server search")]
    public void Taint_NamesSource(string kind, string language, string expected)
    {
        ConfirmationReason reason = ConfirmationReasonText.Taint([new TaintSource(kind, 1)], language);

        Assert.Equal(ConfirmationReasonCode.Taint, reason.Code);
        Assert.Contains(expected, reason.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void Taint_SeveralSourcesJoinedAndNoneFallsBack()
    {
        Assert.Contains("your mail, RSS articles and web pages", ConfirmationReasonText.Taint([new("mail", 1), new("rss", 2), new("web", 3)], "en").Sentence, StringComparison.Ordinal);
        Assert.Contains("deinen E-Mails und Webseiten", ConfirmationReasonText.Taint([new("mail", 1), new("web", 3)], "de").Sentence, StringComparison.Ordinal);
        Assert.Contains("unknown sources", ConfirmationReasonText.Taint([], "en").Sentence, StringComparison.Ordinal);
        Assert.Contains("unbekannten Quellen", ConfirmationReasonText.Taint([], "de").Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "quiet hours")]
    [InlineData("de", "Ruhezeit")]
    public void QuietHoursOverride_Localized(string language, string expected)
    {
        ConfirmationReason reason = ConfirmationReasonText.QuietHoursOverride(language);

        Assert.Equal(ConfirmationReasonCode.QuietHoursOverride, reason.Code);
        Assert.Contains(expected, reason.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "needs your confirmation: unlock")]
    [InlineData("de", "brauche ich deine Bestätigung: unlock")]
    public void Rule_NamesAction(string language, string expected)
    {
        Assert.Contains(expected, ConfirmationReasonText.Rule("unlock", language).Sentence, StringComparison.Ordinal);
    }
}
