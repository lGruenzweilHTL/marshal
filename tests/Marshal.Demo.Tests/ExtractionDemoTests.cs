using Marshal.Abstractions;
using Marshal.Abstractions.Extraction;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Demo.Tests;

/// <summary><c>agent.extract</c> references resolve against the simulated mailbox, feeds and notes in demo mode.</summary>
public sealed class ExtractionDemoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("mail", "demo:m1", "74.70 EUR", "mail")]
    [InlineData("rss", "1760000000000002", "Drying PETG", "rss")]
    [InlineData("note", "recipes/pizza-dough.md", "325 g water", "notes")]
    public async Task Resolvers_ReadTheDemoWorld(string kind, string id, string expected, string taint)
    {
        await using var demo = DemoServices.Create();
        IUntrustedContentResolver resolver = demo.Provider.GetServices<IUntrustedContentResolver>().Single(candidate => candidate.Kind == kind);

        Result<UntrustedItem> item = await resolver.ResolveAsync(id, Ct);

        Assert.True(await resolver.IsAvailableAsync(Ct));
        Assert.Contains(expected, item.Value.Text, StringComparison.Ordinal);
        Assert.Equal(taint, item.Value.TaintKind);
        Assert.Empty(demo.Mail.Calls);
    }

    [Fact]
    public async Task DemoOff_NothingToResolve()
    {
        await using var demo = DemoServices.Create(demo: false);

        foreach (IUntrustedContentResolver resolver in demo.Provider.GetServices<IUntrustedContentResolver>())
            Assert.False(await resolver.IsAvailableAsync(Ct), resolver.Kind);
    }
}
