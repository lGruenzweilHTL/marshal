using Marshal.Abstractions.Rss;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.FreshRss.Tools;

/// <summary>The <c>rss.*</c> tools (a built-in source). They read and mark read; there is no subscribe or delete.</summary>
public sealed class RssToolSource(IRssClient client, IFullTextExtractor extractor) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = RssTools.Create(client, extractor);

    public string Name => "rss";

    public bool IsBuiltIn => true;


    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("rss", "News and RSS feeds: articles, headlines, unread items and digests")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
