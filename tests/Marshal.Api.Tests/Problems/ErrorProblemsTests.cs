using Marshal.Abstractions;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Tests.Problems;

public sealed class ErrorProblemsTests
{
    [Fact]
    public void ToProblem_NotFound_404WithCode()
    {
        ProblemHttpResult problem = new Error("session.not_found", "The conversation does not exist.", ErrorType.NotFound).ToProblem();

        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        Assert.Equal("session.not_found", problem.ProblemDetails.Extensions["code"]);
        Assert.Equal("The conversation does not exist.", problem.ProblemDetails.Detail);
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unavailable, 503)]
    [InlineData(ErrorType.Failure, 422)]
    public void StatusCode_PerErrorType(ErrorType type, int status)
    {
        Assert.Equal(status, ErrorProblems.StatusCode(type));
    }
}
