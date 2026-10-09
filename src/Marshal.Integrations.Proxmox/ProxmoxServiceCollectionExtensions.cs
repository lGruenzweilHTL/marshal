using System.Security.Cryptography;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Proxmox;

/// <summary>
/// Registers the Proxmox integration (<c>proxmox</c> in <c>marshal.yaml</c>): the read-only client and the
/// <c>infra.*</c> tools, which stay hidden until <c>proxmox.url</c> is set.
/// </summary>
public static class ProxmoxServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalProxmox(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProxmoxOptions>().Bind(configuration.GetSection("proxmox"))
            .Validate(options => options.Url is null || options.Url is { IsAbsoluteUri: true, Scheme: "https" }, "proxmox.url must be an absolute https URL.")
            .Validate(options => options.Url is null || ProxmoxOptions.IsTokenId(options.TokenId), "proxmox.tokenId must look like user@realm!name.")
            .Validate(options => options.Url is null || SecretName.IsValid(options.TokenSecret), "proxmox.tokenSecret must name a secret.")
            .Validate(options => options.CertificateFingerprint is null || options.PinnedFingerprint is not null, "proxmox.certificateFingerprint must be a SHA-256 fingerprint (64 hex digits, colons allowed).")
            .ValidateOnStart();

        // Reads only, so retries are safe. A pinned fingerprint replaces chain validation: only that certificate is accepted.
        services.AddHttpClient(ProxmoxRestClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(provider => Handler(provider.GetRequiredService<IOptions<ProxmoxOptions>>().Value.PinnedFingerprint))
            .AddStandardResilienceHandler();
        services.AddSingleton<IProxmoxClient, ProxmoxRestClient>();
        services.AddSingleton<IToolSource, InfraToolSource>();
        return services;
    }

    internal static SocketsHttpHandler Handler(string? pinnedFingerprint)
    {
        var handler = new SocketsHttpHandler();
        if (pinnedFingerprint is { } pinned)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate is not null && Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())) == pinned;
        }

        return handler;
    }
}
