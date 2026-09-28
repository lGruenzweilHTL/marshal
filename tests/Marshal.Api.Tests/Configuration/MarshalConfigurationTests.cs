using System.Collections;
using Marshal.Api.Configuration;
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

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private IConfiguration Build(IDictionary environment) =>
        new ConfigurationBuilder().AddMarshalConfiguration(_directory, environment).Build();

    private void WriteYaml(string yaml) => File.WriteAllText(Path.Combine(_directory, "marshal.yaml"), yaml);
}
