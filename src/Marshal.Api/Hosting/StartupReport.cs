using Marshal.Abstractions.Configuration;

namespace Marshal.Api.Hosting;

/// <summary>
/// Prints a failed start for the operator: configuration and secret errors as a plain list (stack trace only at
/// <c>Debug</c> level), anything else as a one-line summary followed by the stack trace.
/// </summary>
internal static class StartupReport
{
    public static void Write(TextWriter output, Exception error)
    {
        if (Unwrap(error) is { } failure)
        {
            WriteFailure(output, failure);
            return;
        }

        output.WriteLine($"marshal: startup failed: {error.GetType().FullName}: {error.Message}");
        output.WriteLine(error.ToString());
    }

    /// <summary>The effective default log level is <c>Debug</c> or <c>Trace</c>.</summary>
    public static bool IsVerbose(IConfiguration configuration) =>
        Enum.TryParse(configuration["logging:logLevel:default"], ignoreCase: true, out LogLevel level) && level <= LogLevel.Debug;

    private static void WriteFailure(TextWriter output, StartupFailedException failure)
    {
        int count = failure.Errors.Count;
        output.WriteLine($"marshal: startup failed ({count} {(count == 1 ? "error" : "errors")})");
        foreach (StartupError error in failure.Errors)
            output.WriteLine(error.Key is null ? $"  {error.Source}  {error.Message}" : $"  {error.Source}  {error.Key}  {error.Message}");

        if (failure.IncludeStackTrace)
            output.WriteLine(failure.ToString());
    }

    // Host.StartAsync rethrows a single failure as is, but wraps several in an AggregateException.
    private static StartupFailedException? Unwrap(Exception error) => error switch
    {
        StartupFailedException failure => failure,
        AggregateException { InnerExceptions: [StartupFailedException failure] } => failure,
        _ => null,
    };
}
