using System.Text.RegularExpressions;

namespace Marshal.Abstractions.Mail;

/// <summary>
/// The alias rule shared by the account store and the connect flow: 1-32 lowercase letters, digits or '-'.
/// </summary>
public static partial class MailAccountAlias
{
    /// <summary>Trims and lowercases <paramref name="alias"/>, then checks the rule.</summary>
    /// <returns>The normalized alias, or <c>mail.invalid_alias</c>.</returns>
    public static Result<string> Normalize(string? alias)
    {
        string normalized = (alias ?? "").Trim().ToLowerInvariant();
        return Pattern().IsMatch(normalized)
            ? normalized
            : new Error("mail.invalid_alias", "An alias is 1-32 lowercase letters, digits or '-', e.g. private or work.", ErrorType.Validation);
    }

    [GeneratedRegex("^[a-z0-9-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
