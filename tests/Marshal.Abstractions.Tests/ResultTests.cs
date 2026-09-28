namespace Marshal.Abstractions.Tests;

public sealed class ResultTests
{
    private static readonly Error _offline = new("printer.offline", "The printer is offline.", ErrorType.Unavailable);

    [Fact]
    public void Success_HasNoError()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public void Failure_CarriesCodeAndMessage()
    {
        var result = Result.Failure(_offline);

        Assert.True(result.IsFailure);
        Assert.Equal("printer.offline", result.Error.Code);
        Assert.Equal("The printer is offline.", result.Error.Message);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    [Fact]
    public void Error_OnSuccess_Throws()
    {
        var result = Result.Success();

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void GenericFailure_ValueAccess_Throws()
    {
        var result = Result.Failure<int>(_offline);

        Assert.True(result.IsFailure);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void ImplicitConversions_CreateSuccessAndFailure()
    {
        Result<string> success = "ok";
        Result<string> failure = _offline;

        Assert.Equal("ok", success.Value);
        Assert.Equal(_offline, failure.Error);
    }

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        Result<int> result = Result.Success(21).Map(value => value * 2);

        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Map_OnFailure_KeepsError()
    {
        bool called = false;

        Result<int> result = Result.Failure<int>(_offline).Map(value => { called = true; return value * 2; });

        Assert.False(called);
        Assert.Equal(_offline, result.Error);
    }

    [Fact]
    public void Bind_OnSuccess_ChainsFailure()
    {
        Result<string> result = Result.Success(1).Bind(_ => Result.Failure<string>(_offline));

        Assert.Equal(_offline, result.Error);
    }

    [Fact]
    public void Bind_OnFailure_SkipsNext()
    {
        Result<string> result = Result.Failure<int>(_offline).Bind(value => Result.Success(value.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(_offline, result.Error);
    }

    [Fact]
    public void GenericFailure_ConvertsToNonGeneric()
    {
        Result result = Result.Failure<int>(_offline);

        Assert.Equal(_offline, result.Error);
    }

    [Fact]
    public void Match_SelectsBranch()
    {
        string success = Result.Success(3).Match(value => $"v{value}", error => error.Code);
        string failure = Result.Failure<int>(_offline).Match(value => $"v{value}", error => error.Code);

        Assert.Equal("v3", success);
        Assert.Equal("printer.offline", failure);
    }
}
