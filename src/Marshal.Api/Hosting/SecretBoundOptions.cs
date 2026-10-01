using Marshal.Api.Auth;
using Marshal.Core.Models;
using Marshal.Integrations.FreshRss;
using Marshal.Integrations.HomeAssistant;
using Marshal.Integrations.Mail.Credentials;
using Marshal.Integrations.Moonraker;
using Marshal.Integrations.Notes;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Hosting;

/// <summary>Every options object with <c>[Secret]</c> properties, with the key path its section has in configuration.</summary>
internal static class SecretBoundOptions
{
    public static IReadOnlyList<(object Options, string Path)> All(IServiceProvider services) =>
    [
        (Value<ModelRoutingOptions>(services), ""),
        (Value<AuthOptions>(services), "auth"),
        (Value<HomeAssistantOptions>(services), "ha"),
        (Value<PrinterOptions>(services), ""),
        (Value<MailOAuthOptions>(services), "oauth"),
        (Value<FreshRssOptions>(services), "freshrss"),
        (Value<NotesOptions>(services), "notes"),
    ];

    private static T Value<T>(IServiceProvider services)
        where T : class => services.GetRequiredService<IOptions<T>>().Value;
}
