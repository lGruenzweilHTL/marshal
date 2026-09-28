using Marshal.Abstractions.Identity;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Identity;

/// <summary>
/// <c>instance.timeZone</c>, or UTC when it is not set.
/// </summary>
public sealed class ConfiguredInstanceTimeZone(IOptions<InstanceOptions> options) : IInstanceTimeZone
{
    public const string Fallback = "UTC";

    public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult(options.Value.TimeZone ?? Fallback);
}
