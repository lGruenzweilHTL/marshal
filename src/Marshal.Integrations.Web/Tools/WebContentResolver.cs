using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Web;

namespace Marshal.Integrations.Web.Tools;

/// <summary>
/// <c>web:&lt;result id&gt;</c> for <c>agent.extract</c>: a search result of the current session, title and snippet only
/// (taint source <c>web</c>). The page itself is never fetched.
/// </summary>
public sealed class WebContentResolver(IWebSearchProvider provider, ISessionWebResults results, IExecutionContextAccessor executionContext) : IUntrustedContentResolver
{
    public string Kind => "web";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(provider.IsConfigured);

    public Task<Result<UntrustedItem>> ResolveAsync(string id, CancellationToken cancellationToken)
    {
        if (executionContext.Current is not { } context || results.Find(context, id) is not { } result)
            return Task.FromResult<Result<UntrustedItem>>(new Error(ExtractionAnswer.NotFoundCode, $"There is no search result '{id}' in this conversation.", ErrorType.NotFound));

        return Task.FromResult<Result<UntrustedItem>>(new UntrustedItem($"{Kind}:{id}", WebTools.Describe(result), "web"));
    }
}
