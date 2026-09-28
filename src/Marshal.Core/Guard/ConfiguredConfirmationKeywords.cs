using System.Text;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Guard;

/// <summary>
/// Normalizes the full utterance (trim, lowercase, strip punctuation) and compares it with the keyword list of
/// the request language. Only the whole utterance counts, never a keyword inside a longer sentence.
/// </summary>
public sealed class ConfiguredConfirmationKeywords(IOptions<ConfirmationOptions> options) : IConfirmationKeywords
{
    private static readonly Dictionary<string, string[]> _defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = ["confirm", "confirmed", "approved", "yes do it"],
        ["de"] = ["bestätigt", "bestätigen", "ja mach", "ja mach das"],
    };

    public bool IsConfirmation(string utterance, string language)
    {
        string normalized = Normalize(utterance);
        if (normalized.Length == 0)
            return false;

        string key = language.Length >= 2 ? language[..2] : language;
        IEnumerable<string>? keywords = options.Value.Keywords.TryGetValue(key, out List<string>? configured)
            ? configured
            : _defaults.GetValueOrDefault(key);
        return keywords?.Any(keyword => Normalize(keyword) == normalized) ?? false;
    }

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char character in text.Trim().ToLowerInvariant())
        {
            if (char.IsPunctuation(character) || char.IsSymbol(character))
                continue;

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
                builder.Append(' ');

            pendingSpace = false;
            builder.Append(character);
        }

        return builder.ToString();
    }
}
