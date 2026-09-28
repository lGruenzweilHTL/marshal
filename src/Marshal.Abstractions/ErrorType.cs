namespace Marshal.Abstractions;

/// <summary>
/// Category of an expected failure. API endpoints use it to choose the HTTP status code.
/// </summary>
public enum ErrorType
{
    /// <summary>A generic expected failure.</summary>
    Failure,

    /// <summary>The input was invalid.</summary>
    Validation,

    /// <summary>The requested entity does not exist (for the current owner).</summary>
    NotFound,

    /// <summary>The operation conflicts with the current state.</summary>
    Conflict,

    /// <summary>The caller is not allowed to perform the operation.</summary>
    Forbidden,

    /// <summary>A dependency (device, service, model) is not reachable.</summary>
    Unavailable,
}
