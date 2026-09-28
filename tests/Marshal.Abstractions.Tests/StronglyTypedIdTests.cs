using System.Text.Json;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;

namespace Marshal.Abstractions.Tests;

public sealed class StronglyTypedIdTests
{
    private static readonly Guid _value = Guid.Parse("0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b");

    [Fact]
    public void Equality_SameGuid_Equal()
    {
        Assert.Equal(new OwnerId(_value), new OwnerId(_value));
        Assert.NotEqual(OwnerId.New(), OwnerId.New());
    }

    [Fact]
    public void Parse_ValidGuid_ReturnsId()
    {
        Assert.True(SessionId.TryParse(_value.ToString(), null, out SessionId id));
        Assert.Equal(new SessionId(_value), id);
        Assert.Equal(new RunId(_value), RunId.Parse(_value.ToString(), null));
    }

    [Fact]
    public void Parse_InvalidText_Fails()
    {
        Assert.False(PendingActionId.TryParse("not-a-guid", null, out _));
        Assert.False(ApiTokenId.TryParse(null, null, out _));
    }

    [Fact]
    public void ToString_IsGuidText()
    {
        Assert.Equal(_value.ToString(), new ApiTokenId(_value).ToString());
    }

    [Fact]
    public void Json_RoundTrip_UsesPlainGuidString()
    {
        var holder = new Holder(new OwnerId(_value), new RunId(_value));

        string json = JsonSerializer.Serialize(holder);
        Holder? back = JsonSerializer.Deserialize<Holder>(json);

        Assert.Equal($"{{\"Owner\":\"{_value}\",\"Run\":\"{_value}\"}}", json);
        Assert.Equal(holder, back);
    }

    [Fact]
    public void Json_InvalidGuid_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<OwnerId>("\"nope\""));
    }

    [Theory]
    [InlineData("moonraker_voron")]
    [InlineData("a")]
    [InlineData("ha_token2")]
    public void SecretName_ValidName_Created(string name)
    {
        Result<SecretName> result = SecretName.Create(name);

        Assert.True(result.IsSuccess);
        Assert.Equal(name, result.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Moonraker")]
    [InlineData("1abc")]
    [InlineData("with-dash")]
    [InlineData("abcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcdefghijabcde")]
    public void SecretName_InvalidName_Fails(string name)
    {
        Result<SecretName> result = SecretName.Create(name);

        Assert.True(result.IsFailure);
        Assert.Equal("secret.invalid_name", result.Error.Code);
    }

    [Fact]
    public void SecretName_Json_RoundTrip()
    {
        SecretName name = SecretName.Create("freshrss").Value;

        string json = JsonSerializer.Serialize(name);

        Assert.Equal("\"freshrss\"", json);
        Assert.Equal(name, JsonSerializer.Deserialize<SecretName>(json));
    }

    private sealed record Holder(OwnerId Owner, RunId Run);
}
