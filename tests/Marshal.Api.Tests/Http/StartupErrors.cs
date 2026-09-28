namespace Marshal.Api.Tests.Http;

/// <summary>
/// Joins the messages of a host startup failure so tests can assert on the named key.
/// </summary>
internal static class StartupErrors
{
    public static string Flatten(Exception error) => error is AggregateException aggregate
        ? string.Join(" | ", aggregate.Flatten().InnerExceptions.Select(inner => inner.Message))
        : error.Message + (error.InnerException is null ? "" : " | " + Flatten(error.InnerException));
}
