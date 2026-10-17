using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;

namespace Marshal.Core.Guard;

/// <summary>
/// Localized (de / en) reason sentences. Server-determined, so the explanation exists even if the model drops it.
/// </summary>
public static class ConfirmationReasonText
{
    public static ConfirmationReason Rule(string summary, string language) => new(
        ConfirmationReasonCode.Rule,
        IsGerman(language) ? $"Dafür brauche ich deine Bestätigung: {summary}." : $"This needs your confirmation: {summary}.");

    public static ConfirmationReason UnresolvedTarget(string language) => new(
        ConfirmationReasonCode.UnresolvedTarget,
        IsGerman(language) ? "Ich konnte nicht genau erkennen, welches Gerät du meinst." : "I couldn't tell exactly which device you mean.");

    public static ConfirmationReason Taint(IReadOnlyList<TaintSource> sources, string language)
    {
        bool german = IsGerman(language);
        string names = JoinList([.. sources.Select(source => SourceName(source.Kind, german))], german);
        return new ConfirmationReason(
            ConfirmationReasonCode.Taint,
            german
                ? $"Dieses Gespräch enthielt Inhalte aus {names}, und solche Inhalte können versteckte Anweisungen enthalten."
                : $"This conversation included content from {names}, and such content can contain hidden instructions.");
    }

    public static ConfirmationReason QuietHoursOverride(string language) => new(
        ConfirmationReasonCode.QuietHoursOverride,
        IsGerman(language) ? "Das würde dich während deiner Ruhezeit benachrichtigen." : "This would notify you during your quiet hours.");

    private static bool IsGerman(string language) => language.StartsWith("de", StringComparison.OrdinalIgnoreCase);

    private static string SourceName(string kind, bool german) => kind switch
    {
        "mail" => german ? "deinen E-Mails" : "your mail",
        "rss" => german ? "RSS-Artikeln" : "RSS articles",
        "notes" => german ? "deinen Notizen" : "your notes",
        "calendar" => german ? "Kalendereinträgen" : "calendar events",
        "notifications" => german ? "Benachrichtigungen" : "notifications",
        "web" => german ? "Webseiten" : "web pages",
        "event" => german ? "Ereignisdaten" : "event data",
        "attachment" => german ? "angehängten Bildern" : "attached images",
        _ when kind.StartsWith("mcp:", StringComparison.Ordinal) => german ? $"dem Tool-Server {kind[4..]}" : $"the tool server {kind[4..]}",
        _ => kind,
    };

    private static string JoinList(IReadOnlyList<string> items, bool german) => items.Count switch
    {
        0 => german ? "unbekannten Quellen" : "unknown sources",
        1 => items[0],
        _ => $"{string.Join(", ", items.Take(items.Count - 1))} {(german ? "und" : "and")} {items[^1]}",
    };
}
