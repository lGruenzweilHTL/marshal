namespace Marshal.Abstractions.Guard;

/// <summary>
/// What happens with a tool call.
/// </summary>
public enum GuardOutcome
{
    /// <summary>Execute now.</summary>
    Allow,

    /// <summary>Freeze as a pending action and ask the user.</summary>
    RequireConfirmation,

    /// <summary>Never execute; the model receives an error result.</summary>
    Deny,
}
