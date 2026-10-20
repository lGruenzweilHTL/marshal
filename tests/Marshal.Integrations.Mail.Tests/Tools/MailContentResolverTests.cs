using Marshal.Abstractions;
using Marshal.Abstractions.Extraction;
using Marshal.Integrations.Mail.Tools;

namespace Marshal.Integrations.Mail.Tests.Tools;

public sealed class MailContentResolverTests
{
    private readonly FakeMailAccountStore _accounts = new();
    private readonly FakeMailProvider _provider = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private MailContentResolver Resolver() => new(_accounts, [_provider]);

    [Fact]
    public async Task MessageId_ResolvedThroughTheOwnersAccountWithMailTaint()
    {
        _accounts.Add("private");
        _provider.Add("private", "4711", "Your parcel", "Arrives on Tuesday.");

        Result<UntrustedItem> item = await Resolver().ResolveAsync("private:4711", Ct);

        Assert.Equal(("mail", "mail:private:4711"), (item.Value.TaintKind, item.Value.Reference));
        Assert.Contains("Subject: Your parcel", item.Value.Text, StringComparison.Ordinal);
        Assert.Contains("Arrives on Tuesday.", item.Value.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private:9999")]
    [InlineData("work:4711")]
    [InlineData("4711")]
    [InlineData("private:")]
    public async Task UnknownOrForeign_NotFound(string id)
    {
        // "work" is not one of the current owner's accounts: the owner-scoped store does not know it.
        _accounts.Add("private");
        _provider.Add("private", "4711", "Your parcel", "Arrives on Tuesday.");

        Result<UntrustedItem> item = await Resolver().ResolveAsync(id, Ct);

        Assert.Equal(ExtractionAnswer.NotFoundCode, item.Error.Code);
    }

    [Fact]
    public async Task Available_OnlyWithAnAccount()
    {
        Assert.False(await Resolver().IsAvailableAsync(Ct));
        _accounts.Add("private");
        Assert.True(await Resolver().IsAvailableAsync(Ct));
        Assert.Equal("mail", Resolver().Kind);
    }
}
