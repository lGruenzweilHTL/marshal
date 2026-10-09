using System.Text.RegularExpressions;

namespace Marshal.Integrations.Proxmox;

/// <summary>
/// The <c>proxmox</c> section of <c>marshal.yaml</c>: one Proxmox VE cluster, read with an API token that has only the
/// <c>PVEAuditor</c> role. The <c>infra.*</c> tools appear once <see cref="Url"/> is set.
/// </summary>
public sealed partial class ProxmoxOptions
{
    /// <summary>API URL of one cluster node, e.g. <c>https://pve.lan:8006/</c>.</summary>
    public Uri? Url { get; set; }

    /// <summary>API token id, <c>user@realm!name</c>.</summary>
    public string TokenId { get; set; } = "";

    /// <summary>Name of the secret holding the token value (a UUID); read per request.</summary>
    public string? TokenSecret { get; set; }

    /// <summary>SHA-256 fingerprint of the node's certificate (hex, colons optional); pins a self-signed certificate.</summary>
    public string? CertificateFingerprint { get; set; }

    internal bool IsConfigured => Url is not null;

    /// <summary>The fingerprint as 64 uppercase hex digits, or null when unset or malformed.</summary>
    internal string? PinnedFingerprint =>
        CertificateFingerprint is { } value && value.Replace(":", "", StringComparison.Ordinal).Trim().ToUpperInvariant() is { Length: 64 } hex && HexDigits().IsMatch(hex)
            ? hex
            : null;

    internal static bool IsTokenId(string value) => TokenIdPattern().IsMatch(value);

    [GeneratedRegex(@"^[^@\s]+@[^!\s]+![A-Za-z0-9_.-]+$")]
    private static partial Regex TokenIdPattern();

    [GeneratedRegex("^[0-9A-F]+$")]
    private static partial Regex HexDigits();
}
