using Marshal.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Problems;

/// <summary>
/// Maps expected failures to ProblemDetails; the stable error code travels as the <c>code</c> extension.
/// </summary>
internal static class ErrorProblems
{
    public static ProblemHttpResult ToProblem(this Error error) => TypedResults.Problem(
        detail: error.Message,
        statusCode: StatusCode(error.Type),
        title: error.Code,
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static int StatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status422UnprocessableEntity,
    };
}
