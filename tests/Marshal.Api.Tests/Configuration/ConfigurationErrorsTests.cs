using System.Collections;
using Marshal.Abstractions.Configuration;
using Marshal.Api.Configuration;

namespace Marshal.Api.Tests.Configuration;

public sealed class ConfigurationErrorsTests
{
    [Fact]
    public void Locate_KeyFromYaml_NamesMainFileAndKey()
    {
        StartupError error = ConfigurationErrors.Locate("embedding.model is required.", new Hashtable());

        Assert.Equal(new StartupError("marshal.yaml", "embedding.model", "is required."), error);
    }

    [Fact]
    public void Locate_KeySetByEnvironment_NamesTheVariable()
    {
        var environment = new Hashtable { ["MARSHAL__Embedding__Provider"] = "foo" };

        StartupError error = ConfigurationErrors.Locate("embedding.provider must be ollama or openai-compatible.", environment);

        Assert.Equal(new StartupError("MARSHAL__Embedding__Provider", "embedding.provider", "must be ollama or openai-compatible."), error);
    }

    [Fact]
    public void Locate_IndexedKeySetByEnvironment_NamesTheVariable()
    {
        var environment = new Hashtable { ["MARSHAL__Printers__0__ApiKeySecret"] = "voron" };

        StartupError error = ConfigurationErrors.Locate("printers[0].apiKeySecret (voron) does not resolve.", environment);

        Assert.Equal("MARSHAL__Printers__0__ApiKeySecret", error.Source);
        Assert.Equal("printers[0].apiKeySecret", error.Key);
    }

    [Fact]
    public void Locate_OtherVariableSet_NamesMainFile()
    {
        var environment = new Hashtable { ["MARSHAL__Embedding__Model"] = "bge-m3" };

        StartupError error = ConfigurationErrors.Locate("embedding.provider must be ollama or openai-compatible.", environment);

        Assert.Equal("marshal.yaml", error.Source);
    }

    [Fact]
    public void Locate_MessageWithoutKeyPath_NamesMainFileWithoutKey()
    {
        StartupError error = ConfigurationErrors.Locate("Secret values must not come from configuration.", new Hashtable());

        Assert.Equal(new StartupError("marshal.yaml", null, "Secret values must not come from configuration."), error);
    }

    [Fact]
    public void Locate_SingleLowercaseWord_IsNotAKeyPath()
    {
        StartupError error = ConfigurationErrors.Locate("token is missing.", new Hashtable());

        Assert.Null(error.Key);
    }
}
