using Marshal.Abstractions.Suggestions;
using Marshal.Tasks.Suggestions;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Tests.Suggestions;

public sealed class BetaAcceptancePolicyTests
{
    private readonly BetaAcceptancePolicy _policy = new(Options.Create(new SuggestionOptions()));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FirstCandidates_ExploreToTheInbox_EvenWhenAllWereDismissed(int sent) =>
        Assert.Equal(SuggestionDelivery.Inbox, _policy.Decide(new SuggestionDetectorStats("energy_anomaly", sent, 0, sent)));

    [Fact]
    public void BelowMinAcceptance_Dropped()
    {
        var stats = new SuggestionDetectorStats("energy_anomaly", 7, 1, 6);

        Assert.Equal(2.0 / 9, stats.Estimate, 6);
        Assert.Equal(SuggestionDelivery.Drop, _policy.Decide(stats));
    }

    [Fact]
    public void BetweenThresholds_Inbox() =>
        Assert.Equal(SuggestionDelivery.Inbox, _policy.Decide(new SuggestionDetectorStats("window_rain", 4, 1, 3)));

    [Fact]
    public void AtOrAbovePushAcceptance_Push()
    {
        var stats = new SuggestionDetectorStats("window_rain", 6, 5, 1);

        Assert.Equal(6.0 / 8, stats.Estimate, 6);
        Assert.Equal(SuggestionDelivery.Push, _policy.Decide(stats));
        Assert.Equal(SuggestionDelivery.Push, new BetaAcceptancePolicy(Options.Create(new SuggestionOptions { PushAcceptance = 0.75 })).Decide(stats));
    }

    [Fact]
    public void ExpiredSuggestions_CountAsSentOnly_EstimateUnchanged()
    {
        // Three sent, all expired: neither accepted nor dismissed, so the estimate stays at the prior.
        var stats = new SuggestionDetectorStats("print_done_away", 3, 0, 0);

        Assert.Equal(0.5, stats.Estimate, 6);
        Assert.Equal(SuggestionDelivery.Inbox, _policy.Decide(stats));
    }

    [Fact]
    public void Thresholds_FromOptions()
    {
        var strict = new BetaAcceptancePolicy(Options.Create(new SuggestionOptions { Explore = 0, MinAcceptance = 0.6 }));

        Assert.Equal(SuggestionDelivery.Drop, strict.Decide(new SuggestionDetectorStats("window_rain", 0, 0, 0)));
    }
}
