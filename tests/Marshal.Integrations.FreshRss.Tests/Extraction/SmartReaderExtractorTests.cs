using System.Net;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Rss;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.FreshRss.Tests.Extraction;

public sealed class SmartReaderExtractorTests
{
    private static readonly Uri _link = new("https://news.example.com/rust-2");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExtractsTheArticleText()
    {
        var page = new FakePage(Article(paragraphs: 6));

        FullText text = (await Extractor(page).ExtractAsync(_link, Ct)).Value;

        Assert.Equal("Rust 2.0 released", text.Title);
        Assert.Contains("The Rust team announced a new edition, paragraph 3.", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Subscribe to our newsletter", text.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", text.Text, StringComparison.Ordinal);
        Assert.False(text.Truncated);
        Assert.Equal(_link, Assert.Single(page.Requests));
    }

    [Fact]
    public async Task LongText_TruncatedToTheCharacterLimit()
    {
        var page = new FakePage(Article(paragraphs: 40));

        FullText text = (await Extractor(page, limits => limits.MaxCharacters = 500).ExtractAsync(_link, Ct)).Value;

        Assert.True(text.Truncated);
        Assert.Equal(500, text.Text.Length);
    }

    [Fact]
    public async Task PageLargerThanTheLimit_Fails()
    {
        var declared = new FakePage(Article(paragraphs: 40));
        var chunked = new FakePage(Article(paragraphs: 40)) { OmitLength = true };

        Result<FullText> byHeader = await Extractor(declared, limits => limits.MaxBytes = 1024).ExtractAsync(_link, Ct);
        Result<FullText> byBody = await Extractor(chunked, limits => limits.MaxBytes = 1024).ExtractAsync(_link, Ct);

        Assert.Equal("rss.full_text_failed", byHeader.Error.Code);
        Assert.Contains("larger than 1 KB", byHeader.Error.Message, StringComparison.Ordinal);
        Assert.Contains("larger than 1 KB", byBody.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SlowPage_FailsAfterTheTimeLimit()
    {
        var page = new FakePage(Article(paragraphs: 3)) { Delay = TimeSpan.FromSeconds(30) };

        Result<FullText> result = await Extractor(page, limits => limits.Timeout = TimeSpan.FromMilliseconds(200)).ExtractAsync(_link, Ct);

        Assert.Equal("rss.full_text_failed", result.Error.Code);
        Assert.Contains("within", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotHtml_Fails()
    {
        var page = new FakePage("%PDF-1.7", "application/pdf");

        Result<FullText> result = await Extractor(page).ExtractAsync(_link, Ct);

        Assert.Contains("application/pdf", result.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://news.example.com/a")]
    public async Task NonHttpLink_FailsWithoutARequest(string link)
    {
        var page = new FakePage(Article(paragraphs: 3));

        Result<FullText> result = await Extractor(page).ExtractAsync(new Uri(link), Ct);

        Assert.Equal("rss.full_text_failed", result.Error.Code);
        Assert.Empty(page.Requests);
    }

    private static SmartReaderExtractor Extractor(FakePage page, Action<FullTextOptions>? limits = null)
    {
        var options = new FreshRssOptions();
        limits?.Invoke(options.FullText);
        return new SmartReaderExtractor(new PlainHttpClientFactory(page), Options.Create(options));
    }

    private static string Article(int paragraphs)
    {
        var body = new StringBuilder();
        for (int index = 1; index <= paragraphs; index++)
            body.Append(CultureInvariant($"<p>The Rust team announced a new edition, paragraph {index}. It brings faster builds, better diagnostics and a long list of smaller improvements that users asked for.</p>"));

        return $"""
            <html><head><title>Rust 2.0 released</title><script>alert(1)</script></head>
            <body>
              <nav><a href="/">Home</a> <a href="/about">About</a></nav>
              <article><h1>Rust 2.0 released</h1>{body}</article>
              <footer>Subscribe to our newsletter</footer>
            </body></html>
            """;
    }

    private static string CultureInvariant(FormattableString text) => FormattableString.Invariant(text);

    private sealed class FakePage(string body, string contentType = "text/html") : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public TimeSpan Delay { get; init; }

        public bool OmitLength { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            HttpContent content = OmitLength ? new StreamContent(new UnknownLengthStream(bytes)) : new ByteArrayContent(bytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType) { CharSet = "utf-8" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

    // A body whose length is only known by reading it (chunked transfer).
    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
