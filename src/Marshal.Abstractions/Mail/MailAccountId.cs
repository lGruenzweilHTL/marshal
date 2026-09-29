using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Marshal.Abstractions.Mail;

/// <summary>
/// Id of a mail account.
/// </summary>
/// <param name="Value">The wrapped Guid.</param>
[JsonConverter(typeof(GuidIdJsonConverter<MailAccountId>))]
public readonly record struct MailAccountId(Guid Value) : IGuidId<MailAccountId>
{
    /// <summary>Creates a new, time-ordered id.</summary>
    public static MailAccountId New() => new(Guid.CreateVersion7());

    /// <inheritdoc />
    public static MailAccountId From(Guid value) => new(value);

    /// <inheritdoc />
    public static MailAccountId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s, provider));

    /// <inheritdoc />
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out MailAccountId result)
    {
        bool ok = Guid.TryParse(s, provider, out Guid value);
        result = new(value);
        return ok;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToString();
}
