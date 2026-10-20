using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Tools;
using Marshal.Abstractions.Web;

namespace Marshal.Integrations.Web.Tools;

/// <summary>The <c>web.*</c> tools (a built-in source): search and typed answers. No tool fetches a result page.</summary>
public sealed class WebToolSource(IWebSearchProvider provider, ISessionWebResults results, IQuarantinedExtractor extractor) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = WebTools.Create(provider, results, extractor);

    public string Name => "web";

    public bool IsBuiltIn => true;

    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new("web", "Web search: look up facts, news, opening hours, events and anything outside the home")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}
