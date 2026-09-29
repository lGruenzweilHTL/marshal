namespace Marshal.Core.Agent;

/// <summary>
/// Replies the server gives without a model (de / en).
/// </summary>
public static class FixedReplies
{
    public static string StepLimit(string language) => German(language)
        ? "Tut mir leid, das habe ich nicht fertig bekommen."
        : "Sorry, I couldn't finish that.";

    public static string BudgetExceeded(string language) => German(language)
        ? "Abgebrochen: Das Budget dieser Aufgabe ist aufgebraucht."
        : "Stopped: this task's budget is used up.";

    public static string LatencyCap(string language) => German(language)
        ? "Tut mir leid, das dauert zu lange."
        : "Sorry, this is taking too long.";

    public static string LatencyStatus(string language) => German(language)
        ? "Das dauert zu lange, ich breche ab."
        : "This is taking too long, stopping.";

    public static string ModelUnavailable(string language) => German(language)
        ? "Ich kann mein Sprachmodell gerade nicht erreichen."
        : "I can't reach my language model right now.";

    public static string Cancelled(string language) => German(language)
        ? "Okay, abgebrochen."
        : "Okay, cancelled.";

    public static string Expired(string language) => German(language)
        ? "Die Bestätigung ist abgelaufen, ich habe nichts gemacht."
        : "That confirmation expired, so I did nothing.";

    private static bool German(string language) => language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
}
