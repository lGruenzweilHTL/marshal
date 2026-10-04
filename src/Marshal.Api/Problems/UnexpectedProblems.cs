using System.Diagnostics;

namespace Marshal.Api.Problems;

/// <summary>
/// Shapes the ProblemDetails body of an unhandled exception: a stable code, a generic message and the trace id that
/// ties the response to the logged exception. The exception's message, type and stack never reach the body.
/// </summary>
internal static class UnexpectedProblems
{
    public const string Code = "server.unexpected";

    public static void Customize(ProblemDetailsContext context)
    {
        string traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        context.ProblemDetails.Extensions["traceId"] = traceId;
        if (context.Exception is null)
            return;

        context.ProblemDetails.Title = Code;
        context.ProblemDetails.Detail = "Something went wrong on the server.";
        context.ProblemDetails.Extensions["code"] = Code;
        context.ProblemDetails.Extensions.Remove("exception");
    }
}
