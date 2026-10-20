using Marshal.Abstractions;
using Marshal.Abstractions.Extraction;
using Marshal.Integrations.FreshRss.Tools;

namespace Marshal.Integrations.FreshRss.Tests.Tools;

public sealed class RssContentResolverTests
{
    private readonly FakeRssClient _client = new();
    private readonly FakeExtractor _extractor = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RssContentResolver Resolver() => new(_client, _extractor);

    [Fact]
    public async Task ArticleWithLink_FullTextWithRssTaint()
    {
        Result<UntrustedItem> item = await Resolver().ResolveAsync("101", Ct);

        Assert.Equal(("rss", "rss:101"), (item.Value.TaintKind, item.Value.Reference));
        Assert.Contains("The whole article.", item.Value.Text, StringComparison.Ordinal);
        Assert.Equal([new Uri("https://tech.example.com/rust")], _extractor.Links);
    }

    [Fact]
    public async Task ArticleWithoutLink_FeedContent()
    {
        Result<UntrustedItem> item = await Resolver().ResolveAsync("102", Ct);

        Assert.Contains("Graphics cards compared.", item.Value.Text, StringComparison.Ordinal);
        Assert.Empty(_extractor.Links);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("https://evil.example.com/")]
    public async Task UnknownOrNotAnId_NotFoundWithoutFetching(string id)
    {
        Result<UntrustedItem> item = await Resolver().ResolveAsync(id, Ct);

        Assert.Equal(ExtractionAnswer.NotFoundCode, item.Error.Code);
        Assert.Empty(_extractor.Links);
    }

    [Fact]
    public async Task Available_WhenConfigured()
    {
        Assert.True(await Resolver().IsAvailableAsync(Ct));
        _client.IsConfigured = false;
        Assert.False(await Resolver().IsAvailableAsync(Ct));
    }
}
