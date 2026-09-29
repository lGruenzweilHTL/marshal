using System.Collections;
using Marshal.Abstractions;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Templates;
using Marshal.Api.Configuration;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Marshal.Core.Mcp;
using Marshal.Core.Models;
using Marshal.Core.Satellites;
using Marshal.Core.Skills;
using Marshal.Core.Templates;
using Marshal.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Tests.Configuration;

public sealed class ExampleConfigTests
{
    private static readonly string _directory = Path.Combine(RepositoryRoot(), "deploy", "config.example");

    [Theory]
    [InlineData("marshal.yaml")]
    [InlineData("risk.yaml")]
    [InlineData("satellites.yaml")]
    public void ExampleFile_HasNoLiteralSecrets(string file)
    {
        Result<IReadOnlyList<ConfigSecretFinding>> scan = new ConfigSecretScanner().Scan(file, File.ReadAllText(Path.Combine(_directory, file)));

        Assert.True(scan.IsSuccess);
        Assert.Empty(scan.Value);
    }

    [Fact]
    public void ExampleRiskAndSatellites_LoadAndValidate()
    {
        using var risk = YamlConfigFile.Load<RiskRulesFile>(Path.Combine(_directory, "risk.yaml"), new ConfigSecretScanner(), RiskRulesFile.Validate, NullLogger.Instance, watch: false);
        using var satellites = YamlConfigFile.Load<SatellitesFile>(Path.Combine(_directory, "satellites.yaml"), new ConfigSecretScanner(), SatellitesFile.Validate, NullLogger.Instance, watch: false);

        Assert.Equal(3, risk.CurrentValue.Risk.Count);
        Assert.Equal(["mail.*"], satellites.CurrentValue.Satellites["assist_satellite.kids_room"].DenyTools);
    }

    [Fact]
    public void ExampleMarshalYaml_BindsValidModels()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddMarshalConfiguration(_directory, new Hashtable()).Build();
        ModelRoutingOptions models = configuration.Get<ModelRoutingOptions>() ?? new();

        ValidateOptionsResult result = new ModelRoutingOptionsValidator().Validate(null, models);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Contains(models.Models.Values, model => model.Tier == Abstractions.Models.ModelTier.Fast);
        Assert.Contains(models.Models.Values, model => model.Tier == Abstractions.Models.ModelTier.Standard);
        Assert.Equal("ollama", models.Embedding?.Provider);
        Assert.Equal("Europe/Vienna", configuration["instance:timeZone"]);
    }

    [Fact]
    public void ExampleMcpServerSkillAndTemplate_LoadWithoutErrors()
    {
        var scanner = new ConfigSecretScanner();
        using var servers = new YamlMcpServerCatalog(Path.Combine(_directory, "mcp"), scanner, new FakeSecretStore(), NullLogger<YamlMcpServerCatalog>.Instance, watch: false);
        using var skills = new FileSkillCatalog(Path.Combine(_directory, "skills"), null, "skills/**/*", scanner, NullLogger<FileSkillCatalog>.Instance);
        using var templates = new FileTemplateCatalog(Path.Combine(_directory, "templates"), null, "templates/*.md", scanner, NullLogger<FileTemplateCatalog>.Instance);

        Assert.Empty(servers.Errors);
        McpServerDefinition ha = Assert.Single(servers.Servers);
        Assert.Equal(("ha", "ha_token"), (ha.Namespace, ha.AuthTokenSecret?.Value));
        Assert.Empty(skills.Errors);
        Assert.NotEmpty(skills.Skills);
        Assert.Empty(templates.Errors);
        Assert.Contains(templates.Templates, template => template.Name != AgentTemplate.DefaultName);
    }

    [Fact]
    public void RssDigestSkillAndScorerTemplate_LoadAndPassTheSecretScanner()
    {
        var scanner = new ConfigSecretScanner();
        using var skills = new FileSkillCatalog(Path.Combine(_directory, "skills"), null, "skills/**/*", scanner, NullLogger<FileSkillCatalog>.Instance);
        using var templates = new FileTemplateCatalog(Path.Combine(_directory, "templates"), null, "templates/*.md", scanner, NullLogger<FileTemplateCatalog>.Instance);

        Assert.Empty(skills.Errors);
        Assert.Empty(templates.Errors);
        Abstractions.Skills.Skill digest = Assert.Single(skills.Skills, skill => skill.Name == "rss-digest");
        Assert.Contains("agent_delegate", digest.Body, StringComparison.Ordinal);
        Assert.Contains("rss-scorer", digest.Body, StringComparison.Ordinal);
        AgentTemplate scorer = Assert.Single(templates.Templates, template => template.Name == "rss-scorer");
        Assert.Equal(Abstractions.Models.ModelTier.Fast, scorer.ModelTier);
        Assert.Empty(scorer.Tools);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marshal.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
