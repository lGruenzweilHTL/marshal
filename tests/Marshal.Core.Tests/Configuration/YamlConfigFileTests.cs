using Marshal.Abstractions;
using Marshal.Core.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Core.Tests.Configuration;

public sealed class YamlConfigFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-yaml-").FullName;

    private string FilePath => Path.Combine(_directory, "sample.yaml");

    [Fact]
    public void Load_ValidFile_BindsCamelCaseKeys()
    {
        Write("maxItems: 3\nnames:\n  - a\n  - b\n");

        using YamlConfigFile<SampleConfig> file = Load();

        Assert.Equal(3, file.CurrentValue.MaxItems);
        Assert.Equal(["a", "b"], file.CurrentValue.Names);
        Assert.Null(file.LastError);
    }

    [Fact]
    public void Load_MissingFile_UsesDefaults()
    {
        using YamlConfigFile<SampleConfig> file = Load();

        Assert.Equal(1, file.CurrentValue.MaxItems);
    }

    [Fact]
    public void Load_InvalidAtStartup_Throws()
    {
        Write("maxItems: [broken\n");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Load());

        Assert.Contains("sample.yaml", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_LiteralSecretAtStartup_ThrowsWithoutValue()
    {
        Write("maxItems: 2\napiKey: hunter2\n");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Load());

        Assert.Contains("apiKey", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reload_InvalidYaml_KeepsPreviousValueAndRecordsError()
    {
        Write("maxItems: 3\n");
        using YamlConfigFile<SampleConfig> file = Load();
        Write("maxItems: [broken\n");

        Result result = file.Reload();

        Assert.True(result.IsFailure);
        Assert.Equal(3, file.CurrentValue.MaxItems);
        Assert.NotNull(file.LastError);
    }

    [Fact]
    public void Reload_FailingValidation_KeepsPreviousValue()
    {
        Write("maxItems: 3\n");
        using YamlConfigFile<SampleConfig> file = Load();
        Write("maxItems: -1\n");

        Result result = file.Reload();

        Assert.Equal("config.invalid", result.Error.Code);
        Assert.Equal(3, file.CurrentValue.MaxItems);
    }

    [Fact]
    public void Reload_UnknownKey_KeepsPreviousValue()
    {
        Write("maxItems: 3\n");
        using YamlConfigFile<SampleConfig> file = Load();
        Write("maxItem: 4\n");

        Assert.True(file.Reload().IsFailure);
        Assert.Equal(3, file.CurrentValue.MaxItems);
    }

    [Fact]
    public void Reload_LiteralSecret_KeepsPreviousValue()
    {
        Write("maxItems: 3\n");
        using YamlConfigFile<SampleConfig> file = Load();
        Write("maxItems: 4\nnames:\n  - sk-ant-api03-abc\n");

        Assert.True(file.Reload().IsFailure);
        Assert.Equal(3, file.CurrentValue.MaxItems);
    }

    [Fact]
    public void Reload_ValidChange_ReplacesValueNotifiesAndClearsError()
    {
        Write("maxItems: 3\n");
        using YamlConfigFile<SampleConfig> file = Load();
        Write("maxItems: [broken\n");
        file.Reload();
        SampleConfig? notified = null;
        using IDisposable? subscription = file.OnChange((value, _) => notified = value);
        Write("maxItems: 5\n");

        Result result = file.Reload();

        Assert.True(result.IsSuccess);
        Assert.Equal(5, file.CurrentValue.MaxItems);
        Assert.Same(file.CurrentValue, notified);
        Assert.Null(file.LastError);
    }

    [Fact]
    public async Task FileChangedOnDisk_ReloadsWithoutRestart()
    {
        Write("maxItems: 3\n");
        using YamlConfigFile<SampleConfig> file = Load(watch: true);

        Write("maxItems: 7\n");

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (file.CurrentValue.MaxItems != 7 && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.Equal(7, file.CurrentValue.MaxItems);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private YamlConfigFile<SampleConfig> Load(bool watch = false) =>
        YamlConfigFile.Load<SampleConfig>(FilePath, new ConfigSecretScanner(), Validate, NullLogger.Instance, watch);

    private static Result Validate(SampleConfig config) =>
        config.MaxItems > 0 ? Result.Success() : new Error("config.invalid", "maxItems must be positive.", ErrorType.Validation);

    private void Write(string yaml) => File.WriteAllText(FilePath, yaml);

    public sealed class SampleConfig
    {
        public int MaxItems { get; set; } = 1;

        public List<string> Names { get; set; } = [];
    }
}
