using Marshal.Abstractions.Configuration;
using Marshal.Api.Hosting;

namespace Marshal.Api.Tests.Hosting;

public sealed class StartupReportTests
{
    [Fact]
    public void Write_ConfigurationErrors_ListsEachErrorWithSourceAndKey()
    {
        var failure = new StartupFailedException(
        [
            new StartupError("marshal.yaml", "embedding.provider", "must be ollama or openai-compatible."),
            new StartupError("risk.yaml", null, "line 3, column 1: mapping values are not allowed here."),
        ], includeStackTrace: false);
        var output = new StringWriter();

        StartupReport.Write(output, failure);

        Assert.Equal(
            "marshal: startup failed (2 errors)\n" +
            "  marshal.yaml  embedding.provider  must be ollama or openai-compatible.\n" +
            "  risk.yaml  line 3, column 1: mapping values are not allowed here.\n",
            output.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Write_SingleError_UsesSingular()
    {
        var failure = new StartupFailedException([new StartupError("marshal.yaml", "embedding.model", "is required.")], includeStackTrace: false);
        var output = new StringWriter();

        StartupReport.Write(output, failure);

        Assert.StartsWith("marshal: startup failed (1 error)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ConfigurationErrorsWithoutVerbose_PrintsNoStackTrace()
    {
        StartupFailedException failure = Thrown([new StartupError("marshal.yaml", "embedding.model", "is required.")], includeStackTrace: false);
        var output = new StringWriter();

        StartupReport.Write(output, failure);

        Assert.DoesNotContain("   at ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ConfigurationErrorsWithVerbose_AppendsStackTrace()
    {
        StartupFailedException failure = Thrown([new StartupError("marshal.yaml", "embedding.model", "is required.")], includeStackTrace: true);
        var output = new StringWriter();

        StartupReport.Write(output, failure);

        Assert.Contains("   at ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_UnexpectedException_PrintsSummaryAndStackTrace()
    {
        Exception error = Thrown(() => throw new IOException("Address already in use."));
        var output = new StringWriter();

        StartupReport.Write(output, error);

        string[] lines = output.ToString().ReplaceLineEndings("\n").Split('\n');
        Assert.Equal("marshal: startup failed: System.IO.IOException: Address already in use.", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("   at ", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_AggregateOfOneStartupFailure_ReportsTheConfigurationErrors()
    {
        var failure = new StartupFailedException([new StartupError("marshal.yaml", "ha.tokenSecret", "secret 'ha_token' does not resolve.")], includeStackTrace: false);
        var output = new StringWriter();

        StartupReport.Write(output, new AggregateException(failure));

        Assert.StartsWith("marshal: startup failed (1 error)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Message_ListsEveryError()
    {
        var failure = new StartupFailedException(
        [
            new StartupError("marshal.yaml", "embedding.provider", "must be ollama or openai-compatible."),
            new StartupError("marshal.yaml", "embedding.model", "is required."),
        ], includeStackTrace: false);

        Assert.Contains("embedding.provider must be ollama", failure.Message, StringComparison.Ordinal);
        Assert.Contains("embedding.model is required.", failure.Message, StringComparison.Ordinal);
    }

    private static StartupFailedException Thrown(IReadOnlyList<StartupError> errors, bool includeStackTrace) =>
        (StartupFailedException)Thrown(() => throw new StartupFailedException(errors, includeStackTrace));

    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            return error;
        }

        throw new InvalidOperationException("Expected an exception.");
    }
}
