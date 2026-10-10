using System.Collections;
using Marshal.Abstractions;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Templates;
using Marshal.Api.Configuration;
using Marshal.Artifacts;
using Marshal.Core.Configuration;
using Marshal.Core.Displays;
using Marshal.Core.Guard;
using Marshal.Core.Mcp;
using Marshal.Core.Models;
using Marshal.Core.Satellites;
using Marshal.Core.Skills;
using Marshal.Core.Templates;
using Marshal.Tasks.Webhooks;
using Marshal.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    [InlineData("tasks/briefing.yaml")]
    [InlineData("tasks/media-available.yaml")]
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
    public void ExampleMarshalYaml_BindsArtifactOptions()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddMarshalConfiguration(_directory, new Hashtable()).Build();
        using ServiceProvider services = new ServiceCollection().AddMarshalArtifacts(configuration, _ => Path.GetTempPath()).BuildServiceProvider();

        ArtifactOptions options = services.GetRequiredService<IOptions<ArtifactOptions>>().Value;

        Assert.Equal((5120, 200, 15), (options.QuotaMb, options.MaxUploadMb, options.SignedLinkMinutes));
    }

    [Fact]
    public void ExampleMarshalYaml_BindsValidDisplayOptions()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddMarshalConfiguration(_directory, new Hashtable()).Build();
        DisplayOptions options = configuration.GetSection("displays").Get<DisplayOptions>() ?? new();

        ValidateOptionsResult result = new DisplayOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(240, options.LinkMinutes);
        Assert.Empty(options.Surfaces);
    }

    [Fact]
    public void ExampleMarshalYaml_BindsWebhookOptions()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddMarshalConfiguration(_directory, new Hashtable()).Build();

        WebhookOptions options = configuration.GetSection("webhooks").Get<WebhookOptions>()!;

        Assert.Equal((60, 60), (options.DebounceSeconds, options.RequestsPerMinute));
        Assert.Contains("hooks:uptime-kuma", File.ReadAllText(Path.Combine(_directory, "marshal.yaml")), StringComparison.Ordinal);
        Assert.Contains("hooks:proxmox", File.ReadAllText(Path.Combine(_directory, "marshal.yaml")), StringComparison.Ordinal);
        Assert.Contains("hooks:seerr", File.ReadAllText(Path.Combine(_directory, "marshal.yaml")), StringComparison.Ordinal);
        Assert.Contains("# media:", File.ReadAllText(Path.Combine(_directory, "marshal.yaml")), StringComparison.Ordinal);
        Assert.Contains("# github:", File.ReadAllText(Path.Combine(_directory, "marshal.yaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void ExampleConfig_DocumentsMusicSpeakers()
    {
        string marshal = File.ReadAllText(Path.Combine(_directory, "marshal.yaml"));

        Assert.Contains("# music:", marshal, StringComparison.Ordinal);
        Assert.Contains("#   speakers:", marshal, StringComparison.Ordinal);
        Assert.Contains("Assist", marshal[marshal.IndexOf("# music:", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("# speaker: kitchen", File.ReadAllText(Path.Combine(_directory, "satellites.yaml")), StringComparison.Ordinal);
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

    [Fact]
    public void BriefingTemplateAndFormatSkill_Load_TemplateUsesBothSkills()
    {
        var scanner = new ConfigSecretScanner();
        using var skills = new FileSkillCatalog(Path.Combine(_directory, "skills"), null, "skills/**/*", scanner, NullLogger<FileSkillCatalog>.Instance);
        using var templates = new FileTemplateCatalog(Path.Combine(_directory, "templates"), null, "templates/*.md", scanner, NullLogger<FileTemplateCatalog>.Instance);

        Assert.Empty(skills.Errors);
        Assert.Empty(templates.Errors);
        Abstractions.Skills.Skill format = Assert.Single(skills.Skills, skill => skill.Name == "briefing-format");
        Assert.Contains("60 to 90 seconds", format.Body, StringComparison.Ordinal);
        AgentTemplate briefing = Assert.Single(templates.Templates, template => template.Name == "briefing");
        Assert.Equal(["briefing-format", "rss-digest"], briefing.Skills);
        Assert.Contains("rss.mark_read", briefing.Tools);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Marshal.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
