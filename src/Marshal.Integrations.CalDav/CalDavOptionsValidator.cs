using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Marshal.Abstractions.Secrets;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.CalDav;

/// <summary>Checks <c>calendar.caldav</c>; every message names the key it is about.</summary>
internal sealed partial class CalDavOptionsValidator : IValidateOptions<CalDavOptions>
{
    public ValidateOptionsResult Validate(string? name, CalDavOptions options)
    {
        var failures = new List<string>();
        foreach ((string id, CalDavAccountOptions account) in options.Accounts)
        {
            string key = $"calendar.caldav.accounts.{id}";
            if (!AccountId().IsMatch(id))
            {
                failures.Add($"{key}: the account id must match [a-z][a-z0-9_]*.");
                continue;
            }

            if (!IsAllowedUrl(account.Url))
                failures.Add($"{key}.url must be an absolute https URL (http only for a host on the local network).");
            if (string.IsNullOrWhiteSpace(account.Username))
                failures.Add($"{key}.username is required.");
            if (!SecretName.IsValid(account.PasswordSecret))
                failures.Add($"{key}.passwordSecret must be a secret name (^[a-z][a-z0-9_]{{0,63}}$).");
            if (account.Calendars is { } calendars && calendars.Exists(string.IsNullOrWhiteSpace))
                failures.Add($"{key}.calendars must not contain empty names.");
            if (account.PollMinutes is < 1 or > 60)
                failures.Add($"{key}.pollMinutes must be 1-60.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsAllowedUrl(Uri? url) =>
        url is { IsAbsoluteUri: true } && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp && IsLocal(url.Host));

    // Plain http is only acceptable where the password does not cross the internet.
    private static bool IsLocal(string host)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? address))
        {
            if (IPAddress.IsLoopback(address))
                return true;
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return address.IsIPv6UniqueLocal || address.IsIPv6LinkLocal;

            byte[] bytes = address.GetAddressBytes();
            return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 169 && bytes[1] == 254;
        }

        return !host.Contains('.', StringComparison.Ordinal)
            || LocalSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] LocalSuffixes = [".lan", ".local", ".home.arpa", ".internal", ".localdomain"];

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountId();
}
