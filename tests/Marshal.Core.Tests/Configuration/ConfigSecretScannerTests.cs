using Marshal.Abstractions;
using Marshal.Core.Configuration;

namespace Marshal.Core.Tests.Configuration;

public sealed class ConfigSecretScannerTests
{
    private readonly ConfigSecretScanner _scanner = new();

    [Fact]
    public void Scan_LiteralApiKey_FailsNamingFileAndKeyWithoutValue()
    {
        IReadOnlyList<ConfigSecretFinding> findings = Scan("models:\n  claude:\n    apiKey: abc\n");

        ConfigSecretFinding finding = Assert.Single(findings);
        Assert.Equal("marshal.yaml", finding.File);
        Assert.Equal("models.claude.apiKey", finding.KeyPath);
        Assert.Equal(ConfigSecretRule.ForbiddenKey, finding.Rule);
        Assert.DoesNotContain("abc", finding.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("mqttPassword")]
    [InlineData("passphrase")]
    [InlineData("authToken")]
    [InlineData("token")]
    [InlineData("credential")]
    [InlineData("clientCredentials")]
    [InlineData("apiKey")]
    [InlineData("privateKey")]
    [InlineData("awsAccessKey")]
    [InlineData("api_key")]
    public void Scan_ForbiddenKeyWord_Fails(string key)
    {
        IReadOnlyList<ConfigSecretFinding> findings = Scan($"{key}: x\n");

        Assert.Equal(ConfigSecretRule.ForbiddenKey, Assert.Single(findings).Rule);
    }

    [Theory]
    [InlineData("maxTokens: 4096")]
    [InlineData("tokensPerMinute: 10")]
    [InlineData("inputPerMTok: 3")]
    [InlineData("tokenUrl: https://id.example.com/token")]
    [InlineData("keyboardLayout: de")]
    [InlineData("keyName: primary")]
    [InlineData("passwordPolicy: strict")]
    public void Scan_HarmlessTokenWords_Pass(string yaml)
    {
        Assert.Empty(Scan(yaml));
    }

    [Theory]
    [InlineData("moonraker_voron")]
    [InlineData("a")]
    public void Scan_ReferenceKeyWithSecretName_Passes(string name)
    {
        Assert.Empty(Scan($"printers:\n  - name: voron\n    apiKeySecret: {name}\n"));
    }

    [Theory]
    [InlineData("Moonraker-Key")]
    [InlineData("has space")]
    [InlineData("1abc")]
    public void Scan_ReferenceKeyWithNonName_Fails(string value)
    {
        ConfigSecretFinding finding = Assert.Single(Scan($"clientCredentialSecret: \"{value}\"\n"));

        Assert.Equal(ConfigSecretRule.InvalidReference, finding.Rule);
        Assert.Equal("clientCredentialSecret", finding.KeyPath);
    }

    [Fact]
    public void Scan_HexKeyInReference_FailsOnValueScan()
    {
        ConfigSecretFinding finding = Assert.Single(Scan("apiKeySecret: 0a1b2c3d4e5f60718293a4b5c6d7e8f9\n"));

        Assert.Equal(ConfigSecretRule.CredentialValue, finding.Rule);
    }

    [Theory]
    [InlineData("\"-----BEGIN PRIVATE KEY-----\"")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0In0.abc")]
    [InlineData("sk-proj-abcdef")]
    [InlineData("sk-ant-api03-abcdef")]
    [InlineData("ghp_abcdefghijklmnop")]
    [InlineData("xoxb-1234-abcd")]
    [InlineData("xoxp-1234-abcd")]
    [InlineData("0A1B2C3D4E5F60718293A4B5C6D7E8F9AA")]
    [InlineData("k3JH9x2LmQ0pZr7TvW1yB5nC8dF4gH6jK2sL9aX0cV=")]
    [InlineData("q8Zr_T3vW-1yB5nC8dF4gH6jK2sL9aX0cVb7Nm4P")]
    [InlineData("\"prefix k3JH9x2LmQ0pZr7TvW1yB5nC8dF4gH6jK2sL9aX0cV suffix\"")]
    public void Scan_CredentialLookingValue_Fails(string value)
    {
        ConfigSecretFinding finding = Assert.Single(Scan($"notes: {value}\n"));

        Assert.Equal(ConfigSecretRule.CredentialValue, finding.Rule);
        Assert.Equal("notes", finding.KeyPath);
    }

    [Theory]
    [InlineData("Qwen3-30B-A3B-Instruct-2507-GGUF")]
    [InlineData("hf.co/unsloth/Qwen3-30B-A3B-Instruct-2507-GGUF:Q4_K_M")]
    [InlineData("claude-sonnet-4-5-20250929")]
    [InlineData("http://ollama:11434/v1/chat/completions")]
    [InlineData("/config/skills/daily-briefing/SKILL.md")]
    [InlineData("assist_satellite.kitchen_living_room_long_name")]
    [InlineData("\"Cooking area. Timer and recipe questions are common.\"")]
    public void Scan_OrdinaryValues_Pass(string value)
    {
        Assert.Empty(Scan($"value: {value}\n"));
    }

    [Fact]
    public void Scan_ValuesInSequences_AreChecked()
    {
        ConfigSecretFinding finding = Assert.Single(Scan("denyTools:\n  - mail.*\n  - sk-ant-api03-zzz\n"));

        Assert.Equal("denyTools[1]", finding.KeyPath);
    }

    [Fact]
    public void Scan_InvalidYaml_ReturnsFailure()
    {
        Result<IReadOnlyList<ConfigSecretFinding>> result = _scanner.Scan("risk.yaml", "risk: [unclosed\n");

        Assert.True(result.IsFailure);
        Assert.Equal("config.invalid_yaml", result.Error.Code);
    }

    [Fact]
    public void Scan_EmptyFile_Passes()
    {
        Assert.Empty(Scan(""));
    }

    [Fact]
    public void ScanText_CredentialOnALine_FoundWithLineNumber()
    {
        IReadOnlyList<ConfigSecretFinding> findings = _scanner.ScanText("rss/SKILL.md", "# RSS\n\nCall it with sk-ant-api03-abcdefghijklmnop please.\n");

        ConfigSecretFinding finding = Assert.Single(findings);
        Assert.Equal("line 3", finding.KeyPath);
    }

    [Fact]
    public void ScanText_OrdinaryMarkdown_NoFindings()
    {
        Assert.Empty(_scanner.ScanText("rss/SKILL.md", "# RSS digest\n\n1. Search unread articles with rss_search.\n2. See https://example.org/docs/format-guide for details.\n"));
    }

    private IReadOnlyList<ConfigSecretFinding> Scan(string yaml) => _scanner.Scan("marshal.yaml", yaml).Value;
}
