using System.Collections;
using Marshal.Abstractions.Configuration;
using Marshal.Api.Configuration;
using Marshal.Api.Hosting;
using Microsoft.Extensions.Configuration;

namespace Marshal.Api.Tests.Configuration;

public sealed class MarshalConfigurationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-config-").FullName;

    [Fact]
    public void Build_EnvironmentOverridesYaml()
    {
        WriteYaml("http:\n  pathBase: /a\n");

        IConfiguration configuration = Build(new Hashtable { ["MARSHAL__Http__PathBase"] = "/b" });

        Assert.Equal("/b", configuration["http:pathBase"]);
    }

    [Fact]
    public void Build_YamlWithoutEnvironment_UsesYaml()
    {
        WriteYaml("http:\n  pathBase: /a\n");

        IConfiguration configuration = Build(new Hashtable());

        Assert.Equal("/a", configuration["Http:PathBase"]);
    }

    [Fact]
    public void Build_SecretVariables_NeverEnterConfiguration()
    {
        WriteYaml("");

        IConfiguration configuration = Build(new Hashtable
        {
            ["MARSHAL_SECRET_MOONRAKER_VORON"] = "super-secret-value",
            ["MARSHAL_OTHER"] = "single-underscore",
            ["PATH"] = "/usr/bin",
        });

        Assert.DoesNotContain(configuration.AsEnumerable(), pair => pair.Value is "super-secret-value" or "single-underscore" or "/usr/bin");
    }

    [Fact]
    public void Build_MissingYaml_IsAllowed()
    {
        IConfiguration configuration = Build(new Hashtable { ["MARSHAL__Http__PathBase"] = "/b" });

        Assert.Equal("/b", configuration["http:pathBase"]);
    }

    [Fact]
    public void Build_InvalidYaml_ReportsMainFileWithPositionAndNoValue()
    {
        WriteYaml("http:\n  pathBase: /a\nembedding: [sk-ant-literal\n");

        Exception error = Assert.ThrowsAny<Exception>(() => Build(new Hashtable()));

        StartupFailedException failure = Assert.IsType<StartupFailedException>(error);
        StartupError reported = Assert.Single(failure.Errors);
        Assert.Equal("marshal.yaml", reported.Source);
        Assert.Null(reported.Key);
        Assert.Contains("line ", reported.Message, StringComparison.Ordinal);
        Assert.Contains("column ", reported.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-ant-literal", reported.Message, StringComparison.Ordinal);
        Assert.False(failure.IncludeStackTrace);
    }

    [Fact]
    public void Build_InvalidYamlWithDebugVariable_IncludesStackTrace()
    {
        WriteYaml("embedding: [\n");

        Exception error = Assert.ThrowsAny<Exception>(() => Build(new Hashtable { ["MARSHAL__Logging__LogLevel__Default"] = "Debug" }));

        Assert.True(Assert.IsType<StartupFailedException>(error).IncludeStackTrace);
    }

    [Fact]
    public void Build_Defaults_HideTheHostsOwnStartupFailureLog()
    {
        IConfiguration configuration = Build(new Hashtable());

        Assert.Equal("Critical", configuration["logging:logLevel:Microsoft.Extensions.Hosting.Internal.Host"]);
    }

    [Fact]
    public void Build_YamlLogLevelForHost_OverridesDefault()
    {
        WriteYaml("logging:\n  logLevel:\n    Microsoft.Extensions.Hosting.Internal.Host: Information\n");

        IConfiguration configuration = Build(new Hashtable());

        Assert.Equal("Information", configuration["logging:logLevel:Microsoft.Extensions.Hosting.Internal.Host"]);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private IConfiguration Build(IDictionary environment) =>
        new ConfigurationBuilder().AddMarshalConfiguration(_directory, environment).Build();

    private void WriteYaml(string yaml) => File.WriteAllText(Path.Combine(_directory, "marshal.yaml"), yaml);
}
