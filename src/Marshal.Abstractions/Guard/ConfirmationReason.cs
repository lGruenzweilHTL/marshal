namespace Marshal.Abstractions.Guard;

/// <summary>
/// One reason a call needs confirmation: a stable code and a short sentence in the request language.
/// </summary>
/// <param name="Code">Reason code.</param>
/// <param name="Sentence">Sentence shown or spoken to the user.</param>
public sealed record ConfirmationReason(ConfirmationReasonCode Code, string Sentence);
