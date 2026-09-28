namespace Marshal.Core.Guard;

/// <summary>
/// Deterministic confirmation by exact keyword match on the whole utterance.
/// </summary>
public interface IConfirmationKeywords
{
    bool IsConfirmation(string utterance, string language);
}
