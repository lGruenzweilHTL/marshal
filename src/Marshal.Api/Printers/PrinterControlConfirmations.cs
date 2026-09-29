using System.Collections.Concurrent;
using System.Security.Cryptography;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Printers;

namespace Marshal.Api.Printers;

/// <summary>
/// Confirm-tier printer controls frozen server-side until the user confirms them in the dialog: the command,
/// printer and user are stored under a random single-use token that expires after <see cref="Lifetime"/>.
/// </summary>
internal sealed class PrinterControlConfirmations(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, Frozen> _pending = new(StringComparer.Ordinal);

    public (string Token, DateTimeOffset ExpiresAt) Freeze(OwnerId owner, string printer, PrinterCommand command)
    {
        DateTimeOffset now = time.GetUtcNow();
        foreach ((string token, Frozen frozen) in _pending)
        {
            if (frozen.ExpiresAt <= now)
                _pending.TryRemove(token, out _);
        }

        string created = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        DateTimeOffset expiresAt = now + Lifetime;
        _pending[created] = new Frozen(owner, printer, command, expiresAt);
        return (created, expiresAt);
    }

    /// <summary>The frozen command, once: the token is removed whether it matches or not.</summary>
    public PrinterCommand? Take(OwnerId owner, string printer, string token)
    {
        if (!_pending.TryRemove(token, out Frozen? frozen))
            return null;

        return frozen.Owner == owner && string.Equals(frozen.Printer, printer, StringComparison.OrdinalIgnoreCase) && frozen.ExpiresAt > time.GetUtcNow()
            ? frozen.Command
            : null;
    }

    private sealed record Frozen(OwnerId Owner, string Printer, PrinterCommand Command, DateTimeOffset ExpiresAt);
}
