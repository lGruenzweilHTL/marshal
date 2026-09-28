namespace Marshal.Abstractions.Guard;

/// <summary>
/// Why a call needs confirmation. Determined by the guard, never by the model.
/// </summary>
public enum ConfirmationReasonCode
{
    /// <summary>A risk rule (or the tool's default tier) makes the call confirm tier.</summary>
    Rule,

    /// <summary>The call's targets could not be resolved to known devices.</summary>
    UnresolvedTarget,

    /// <summary>The run read untrusted content and the tool persists state.</summary>
    Taint,

    /// <summary>The call overrides quiet hours.</summary>
    QuietHoursOverride,
}
