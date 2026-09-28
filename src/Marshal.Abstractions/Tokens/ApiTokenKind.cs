namespace Marshal.Abstractions.Tokens;

/// <summary>
/// Kind of machine token.
/// </summary>
public enum ApiTokenKind
{
    /// <summary>A normal scoped token.</summary>
    Standard,

    /// <summary>A single-purpose callback token for one executor session.</summary>
    ExecutorCallback,
}
