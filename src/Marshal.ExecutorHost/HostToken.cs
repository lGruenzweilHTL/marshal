using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Marshal.ExecutorHost;

/// <summary>
/// Checks Marshal's bearer token against the token file (constant-time). The file is read on each check, so the
/// token can be rotated without a restart.
/// </summary>
public sealed class HostToken(IOptions<ExecutorHostOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        string header = context.HttpContext.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal) || !await MatchesAsync(header["Bearer ".Length..].Trim(), context.HttpContext.RequestAborted))
            return Results.Unauthorized();

        return await next(context);
    }

    private async Task<bool> MatchesAsync(string presented, CancellationToken cancellationToken)
    {
        if (!File.Exists(options.Value.TokenFile))
            return false;

        string expected = (await File.ReadAllTextAsync(options.Value.TokenFile, cancellationToken)).Trim();
        return expected.Length > 0 && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }
}
