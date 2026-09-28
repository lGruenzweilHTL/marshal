using System.Globalization;
using System.Text;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Satellites;

namespace Marshal.Core.Context;

/// <summary>
/// A few lines about where the turn was spoken: area, floor, satellite notes and verbosity.
/// </summary>
public sealed class SatelliteContextContributor(ISatelliteContextProvider satellites) : IContextContributor
{
    public int Order => 300;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        if (request.Context.SatelliteId is null && request.Context.AreaId is null)
            return null;

        SatelliteContext context = await satellites.GetContextAsync(request.Context, cancellationToken);
        var text = new StringBuilder();
        if (context.AreaName is not null)
            text.AppendLine(CultureInfo.InvariantCulture, $"The user is speaking in the {context.AreaName}{(context.FloorName is null ? "" : $" ({context.FloorName})")}. Requests like \"here\" or \"the light\" refer to this area.");
        if (context.Profile.Verbosity != SatelliteProfile.Default.Verbosity)
            text.AppendLine(CultureInfo.InvariantCulture, $"Preferred reply length on this device: {context.Profile.Verbosity}.");
        if (!string.IsNullOrWhiteSpace(context.Profile.Notes))
            text.AppendLine(CultureInfo.InvariantCulture, $"About this device: {context.Profile.Notes}");

        return text.Length == 0 ? null : text.ToString();
    }
}
