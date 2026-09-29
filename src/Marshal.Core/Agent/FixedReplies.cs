namespace Marshal.Core.Agent;

/// <summary>
/// Replies the server gives without a model (de / en).
/// </summary>
public static class FixedReplies
{
    public static string StepLimit(string language) => German(language)
        ? "Tut mir leid, das habe ich nicht fertig bekommen."
        : "Sorry, I couldn't finish that.";

    public static string AwaitingApproval(string language) => German(language)
        ? "Wartet auf deine Freigabe."
        : "Waiting for your approval.";

    public static string BudgetExceeded(string language) => German(language)
        ? "Abgebrochen: Das Budget dieser Aufgabe ist aufgebraucht."
        : "Stopped: this task's budget is used up.";

    public static string LatencyCap(string language) => German(language)
        ? "Tut mir leid, das dauert zu lange."
        : "Sorry, this is taking too long.";

    public static string LatencyStatus(string language) => German(language)
        ? "Das dauert zu lange, ich breche ab."
        : "This is taking too long, stopping.";

    public static string ApprovalGiven(string language) => German(language)
        ? "Freigegeben."
        : "Approved.";

    public static string ApprovalClosed(string language) => German(language)
        ? "Diese Freigabe ist nicht mehr offen."
        : "That approval is no longer open.";

    public static string ReplyTaken(string language) => German(language)
        ? "Okay, ich kümmere mich darum."
        : "Got it, I'll take it from here.";

    public static string Backgrounded(string language) => German(language)
        ? "Ich arbeite daran und sage dir Bescheid, wenn ich fertig bin."
        : "I'm working on it and will let you know when it's done.";

    public static string BackgroundUnavailable(string language) => German(language)
        ? "Das kann ich gerade nicht im Hintergrund weitermachen."
        : "I can't continue this in the background right now.";

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
