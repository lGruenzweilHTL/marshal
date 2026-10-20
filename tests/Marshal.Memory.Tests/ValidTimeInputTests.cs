using Marshal.Abstractions;

namespace Marshal.Memory.Tests;

public sealed class ValidTimeInputTests
{
    private static readonly TimeZoneInfo _vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    [Fact]
    public void Parse_Date_MidnightInOwnerTimeZone()
    {
        Result<DateTimeOffset?> parsed = ValidTimeInput.Parse("2026-03-01", _vienna, "validFrom");

        Assert.Equal(new DateTimeOffset(2026, 2, 28, 23, 0, 0, TimeSpan.Zero), parsed.Value);
    }

    [Fact]
    public void Parse_DateTimeWithoutOffset_OwnerTimeZone()
    {
        Result<DateTimeOffset?> parsed = ValidTimeInput.Parse("2026-07-01T08:30", _vienna, "validFrom");

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 6, 30, 0, TimeSpan.Zero), parsed.Value);
    }

    [Fact]
    public void Parse_DateTimeWithOffset_Kept()
    {
        Result<DateTimeOffset?> parsed = ValidTimeInput.Parse("2026-07-01T08:30:00Z", _vienna, "validTo");

        Assert.Equal(new DateTimeOffset(2026, 7, 1, 8, 30, 0, TimeSpan.Zero), parsed.Value);
    }

    [Fact]
    public void Parse_Null_NoValue()
    {
        Assert.Null(ValidTimeInput.Parse(null, _vienna, "validFrom").Value);
    }

    [Theory]
    [InlineData("March")]
    [InlineData("2026-13-01")]
    [InlineData("01.03.2026")]
    public void Parse_Invalid_ValidationErrorNamingTheArgument(string text)
    {
        Result<DateTimeOffset?> parsed = ValidTimeInput.Parse(text, _vienna, "validFrom");

        Assert.Equal(ErrorType.Validation, parsed.Error.Type);
        Assert.Contains("validFrom", parsed.Error.Message, StringComparison.Ordinal);
    }
}
