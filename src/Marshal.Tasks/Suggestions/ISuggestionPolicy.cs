using Marshal.Abstractions.Suggestions;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Suggestions;

/// <summary>Decides whether and how a detector's candidate is sent.</summary>
public interface ISuggestionPolicy
{
    SuggestionDelivery Decide(SuggestionDetectorStats stats);
}

/// <summary>
/// The Beta(1,1) estimate (accepted + 1) / (accepted + dismissed + 2): the first <c>explore</c> candidates go to the
/// inbox, then below <c>minAcceptance</c> nothing is sent, from <c>pushAcceptance</c> on it is pushed, in between it
/// goes to the inbox. Expired suggestions count as sent only, so they never move the estimate.
/// </summary>
public sealed class BetaAcceptancePolicy(IOptions<SuggestionOptions> options) : ISuggestionPolicy
{
    public SuggestionDelivery Decide(SuggestionDetectorStats stats)
    {
        SuggestionOptions settings = options.Value;
        if (stats.Sent < settings.Explore)
            return SuggestionDelivery.Inbox;

        double estimate = stats.Estimate;
        if (estimate < settings.MinAcceptance)
            return SuggestionDelivery.Drop;
        return estimate >= settings.PushAcceptance ? SuggestionDelivery.Push : SuggestionDelivery.Inbox;
    }
}
