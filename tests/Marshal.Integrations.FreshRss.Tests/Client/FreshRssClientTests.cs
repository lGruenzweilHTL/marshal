using Marshal.Abstractions;
using Marshal.Abstractions.Rss;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.FreshRss.Tests.Client;

public sealed class FreshRssClientTests(FreshRssServer server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListFeeds_WithCategories()
    {
        FreshRssClient client = await ClientAsync();

        IReadOnlyList<RssFeed> feeds = (await client.ListFeedsAsync(Ct)).Value;

        Assert.Equal(["Science Daily", "Tech News"], feeds.Select(feed => feed.Title).Order(StringComparer.Ordinal));
        RssFeed tech = feeds.Single(feed => feed.Title == "Tech News");
        Assert.Equal(["Tech"], tech.Categories);
        Assert.Equal("http://localhost/feeds/tech.xml", tech.Url);
    }

    [Fact]
    public async Task ListCategories_OnlyFoldersIncludingTheDefault()
    {
        FreshRssClient client = await ClientAsync();

        IReadOnlyList<RssCategory> categories = (await client.ListCategoriesAsync(Ct)).Value;

        Assert.Equal(["Science", "Tech", "Uncategorized"], categories.Select(category => category.Label).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetUnread_FollowsContinuationUntilTheLimit()
    {
        FreshRssClient client = await ClientAsync(pageSize: 2);

        IReadOnlyList<RssArticle> all = (await client.GetUnreadAsync(null, null, 10, Ct)).Value;
        IReadOnlyList<RssArticle> three = (await client.GetUnreadAsync(null, null, 3, Ct)).Value;

        Assert.Equal(5, all.Count);
        Assert.Equal(5, all.Select(article => article.Id).Distinct().Count());
        Assert.Equal(all.Take(3).Select(article => article.Id), three.Select(article => article.Id));
        Assert.All(all, article => Assert.True(article.Unread));
    }

    [Fact]
    public async Task GetUnread_ByCategoryAndSince()
    {
        FreshRssClient client = await ClientAsync();

        IReadOnlyList<RssArticle> tech = (await client.GetUnreadAsync("Tech", null, 10, Ct)).Value;
        IReadOnlyList<RssArticle> future = (await client.GetUnreadAsync(null, DateTimeOffset.UtcNow.AddHours(1), 10, Ct)).Value;
        IReadOnlyList<RssArticle> past = (await client.GetUnreadAsync(null, DateTimeOffset.UtcNow.AddHours(-1), 10, Ct)).Value;

        Assert.Equal(["Kernel 7.1 notes", "New GPU benchmarks", "Rust 2.0 released"], tech.Select(article => article.Title).Order(StringComparer.Ordinal));
        Assert.All(tech, article => Assert.Equal("Tech News", article.FeedTitle));
        Assert.Empty(future);
        Assert.Equal(5, past.Count);
    }

    [Fact]
    public async Task GetArticle_SummaryAsPlainTextWithLink()
    {
        FreshRssClient client = await ClientAsync();
        string id = (await client.GetUnreadAsync("Tech", null, 10, Ct)).Value.Single(article => article.Title == "Rust 2.0 released").Id;

        RssArticle article = (await client.GetArticleAsync(id, Ct)).Value;

        Assert.Matches("^[0-9]+$", article.Id);
        Assert.Equal(id, article.Id);
        Assert.Equal(new Uri("http://localhost/feeds/rust.html"), article.Link);
        Assert.Equal("The Rust team announced a new edition.", article.Summary);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero), article.Published);
        Assert.Equal(["Tech"], article.Categories);
    }

    [Fact]
    public async Task GetArticle_Unknown_NotFound()
    {
        FreshRssClient client = await ClientAsync();

        Result<RssArticle> missing = await client.GetArticleAsync("12345", Ct);

        Assert.Equal("rss.article_not_found", missing.Error.Code);
    }

    [Fact]
    public async Task Search_MatchesTitleAndSummaryWords()
    {
        FreshRssClient client = await ClientAsync();

        IReadOnlyList<RssArticle> rust = (await client.SearchAsync("rust edition", 10, Ct)).Value;
        IReadOnlyList<RssArticle> none = (await client.SearchAsync("rust comet", 10, Ct)).Value;

        Assert.Equal("Rust 2.0 released", Assert.Single(rust).Title);
        Assert.Empty(none);
    }

    [Fact]
    public async Task MarkRead_TwoArticles()
    {
        FreshRssClient client = await ClientAsync();
        IReadOnlyList<RssArticle> unread = (await client.GetUnreadAsync(null, null, 10, Ct)).Value;

        Result marked = await client.MarkReadAsync([unread[0].Id, unread[1].Id], Ct);

        Assert.True(marked.IsSuccess);
        Assert.Equal(3, (await client.GetUnreadAsync(null, null, 10, Ct)).Value.Count);
        Assert.False((await client.GetArticleAsync(unread[0].Id, Ct)).Value.Unread);
    }

    [Fact]
    public async Task WrongPassword_AuthFailed()
    {
        FreshRssOptions options = await server.CreateUserAsync();
        options.ApiPassword = "wrong";
        var client = new FreshRssClient(new PlainHttpClientFactory(), Options.Create(options));

        Result<IReadOnlyList<RssFeed>> feeds = await client.ListFeedsAsync(Ct);

        Assert.Equal("rss.auth_failed", feeds.Error.Code);
    }

    [Fact]
    public async Task NotConfigured()
    {
        var client = new FreshRssClient(new PlainHttpClientFactory(), Options.Create(new FreshRssOptions()));

        Assert.False(client.IsConfigured);
        Assert.Equal("rss.not_configured", (await client.ListFeedsAsync(Ct)).Error.Code);
    }

    private async Task<FreshRssClient> ClientAsync(int pageSize = 100)
    {
        FreshRssOptions options = await server.CreateUserAsync();
        options.PageSize = pageSize;
        return new FreshRssClient(new PlainHttpClientFactory(), Options.Create(options));
    }
}
