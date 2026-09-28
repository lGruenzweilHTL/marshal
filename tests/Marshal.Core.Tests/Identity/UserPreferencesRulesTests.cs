using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Core.Identity;

namespace Marshal.Core.Tests.Identity;

public sealed class UserPreferencesRulesTests
{
    private static readonly UserPreferencesRecord _valid = new(
        "de",
        "Europe/Vienna",
        [new QuietHoursWindow(DayOfWeek.Monday, new TimeOnly(22, 0), new TimeOnly(7, 0))],
        new NotificationTargets("person.lukas", ["notify.mobile_app_pixel"]),
        null);

    [Fact]
    public void Validate_ValidPreferences_Succeeds() => Assert.True(UserPreferencesRules.Validate(_valid).IsSuccess);

    [Theory]
    [InlineData("Mars/Base")]
    [InlineData("")]
    [InlineData("Not a zone")]
    public void Validate_UnknownTimeZone_FailsNamingIt(string zone)
    {
        Result result = UserPreferencesRules.Validate(_valid with { TimeZone = zone });

        Assert.Equal("preferences.invalid", result.Error.Code);
        Assert.Contains("timeZone", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_UnsupportedLanguage_Fails() =>
        Assert.Contains("language", UserPreferencesRules.Validate(_valid with { Language = "fr" }).Error.Message, StringComparison.Ordinal);

    [Fact]
    public void Validate_EmptyQuietWindow_Fails() =>
        Assert.True(UserPreferencesRules.Validate(_valid with { QuietHours = [new QuietHoursWindow(DayOfWeek.Sunday, new TimeOnly(8, 0), new TimeOnly(8, 0))] }).IsFailure);

    [Theory]
    [InlineData("lukas", null)]
    [InlineData("person.lukas", "mobile_app_pixel")]
    public void Validate_BadTargets_Fail(string person, string? push) =>
        Assert.True(UserPreferencesRules.Validate(_valid with { NotificationTargets = new NotificationTargets(person, push is null ? [] : [push]) }).IsFailure);

    [Theory]
    [InlineData("UTC", true)]
    [InlineData("Europe/Vienna", true)]
    [InlineData("Mars/Base", false)]
    [InlineData(null, false)]
    public void IsIanaTimeZone(string? zone, bool expected) => Assert.Equal(expected, UserPreferencesRules.IsIanaTimeZone(zone));
}
