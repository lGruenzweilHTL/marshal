namespace Marshal.Abstractions;

/// <summary>
/// An expected failure with a stable, machine-readable code (e.g. <c>printer.offline</c>)
/// and a human-readable message.
/// </summary>
/// <param name="Code">Stable error code, <c>&lt;area&gt;.&lt;reason&gt;</c>.</param>
/// <param name="Message">Human-readable description. Never contains secrets.</param>
/// <param name="Type">Category used to map the error to a transport status.</param>
public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure);
