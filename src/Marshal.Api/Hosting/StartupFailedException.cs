using Marshal.Abstractions.Configuration;

namespace Marshal.Api.Hosting;

/// <summary>
/// A startup stage found configuration or secret errors. Thrown once per stage, after every check of the stage ran,
/// so <c>Program</c> can report the whole list; the checks themselves return their errors.
/// </summary>
internal sealed class StartupFailedException(IReadOnlyList<StartupError> errors, bool includeStackTrace, Exception? cause = null)
    : Exception("Startup failed: " + string.Join(" | ", errors), cause)
{
    public IReadOnlyList<StartupError> Errors { get; } = errors;

    /// <summary>The configured log level is <c>Debug</c> or below, so the report includes the stack trace.</summary>
    public bool IncludeStackTrace { get; } = includeStackTrace;
}
