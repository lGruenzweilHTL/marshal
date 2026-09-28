namespace Marshal.Abstractions.Audit;

/// <summary>
/// Who performed an audited action.
/// </summary>
public enum AuditActorKind
{
    /// <summary>A logged-in user.</summary>
    User,

    /// <summary>A machine token.</summary>
    ApiToken,

    /// <summary>Marshal itself or an operator CLI command.</summary>
    System,
}
