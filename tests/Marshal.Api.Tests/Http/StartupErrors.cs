using Marshal.Api.Hosting;

namespace Marshal.Api.Tests.Http;

/// <summary>
/// Joins the messages of a host startup failure so tests can assert on the named key.
/// </summary>
internal static class StartupErrors
{
    public static string Flatten(Exception error) => error is AggregateException aggregate
        ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
        : error.Message + (error.InnerException is null ? "" : " | " + Flatten(error.InnerException));

    /// <summary>The reported startup failure inside <paramref name="error"/>; fails the test when there is none.</summary>
    public static StartupFailedException Reported(Exception error) => Find(error)
        ?? throw new Xunit.Sdk.XunitException($"Expected a reported startup failure, got: {error}");

    private static StartupFailedException? Find(Exception? error) => error switch
    {
        null => null,
        StartupFailedException failure => failure,
        AggregateException aggregate => aggregate.Flatten().InnerExceptions.Select(Find).FirstOrDefault(found => found is not null),
        _ => Find(error.InnerException),
    };
}
