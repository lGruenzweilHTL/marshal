using Marshal.Core.Displays;
using Marshal.Tasks.Notifications;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Displays;

/// <summary>Displays need a URL the TV can reach: <c>displays.mediaBaseUrl</c> or, as fallback, <c>notifications.publicUrl</c>.</summary>
internal sealed class DisplayBaseUrlValidator(IOptions<NotificationOptions> notifications) : IValidateOptions<DisplayOptions>
{
    public ValidateOptionsResult Validate(string? name, DisplayOptions options) =>
        options.Surfaces.Count > 0 && options.MediaBaseUrl is null && notifications.Value.PublicUrl is null
            ? ValidateOptionsResult.Fail("displays.mediaBaseUrl is required when displays are configured (or set notifications.publicUrl).")
            : ValidateOptionsResult.Success;
}
