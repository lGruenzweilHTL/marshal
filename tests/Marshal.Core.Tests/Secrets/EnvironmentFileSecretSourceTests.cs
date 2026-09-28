using System.Collections;
using Marshal.Abstractions.Secrets;
using Marshal.Core.Secrets;

namespace Marshal.Core.Tests.Secrets;

public sealed class EnvironmentFileSecretSourceTests : IDisposable
{
    private static readonly SecretName _voron = SecretName.Create("moonraker_voron").Value;

    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-secrets-").FullName;

    [Fact]
    public async Task List_EnvironmentVariablesAndFiles_AreInstanceSecretsWithValidNames()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "moonraker_voron"), "from-file", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_directory, ".hidden"), "x", TestContext.Current.CancellationToken);
        var source = new EnvironmentFileSecretSource(new Hashtable { ["MARSHAL_SECRET_HA_TOKEN"] = "v", ["MARSHAL_SECRET_BAD-NAME"] = "v", ["PATH"] = "/bin" }, _directory);

        IReadOnlyList<SecretListing> listed = await source.ListAsync(Marshal.Testing.TestExecutionContext.OwnerA, TestContext.Current.CancellationToken);

        Assert.Equal(["ha_token", "moonraker_voron"], listed.Select(entry => entry.Name.Value));
        Assert.All(listed, entry => Assert.Equal((null, SecretOrigin.Environment), (entry.Owner, entry.Origin)));
    }

    [Fact]
    public async Task List_MissingDirectory_ListsEnvironmentOnly()
    {
        var source = new EnvironmentFileSecretSource(new Hashtable { ["MARSHAL_SECRET_HA_TOKEN"] = "v" }, Path.Combine(_directory, "missing"));

        IReadOnlyList<SecretListing> listed = await source.ListAsync(Marshal.Testing.TestExecutionContext.OwnerA, TestContext.Current.CancellationToken);

        Assert.Equal(["ha_token"], listed.Select(entry => entry.Name.Value));
    }

    [Fact]
    public async Task TryGet_EnvironmentVariable_UsesUpperCasedName()
    {
        var source = new EnvironmentFileSecretSource(new Hashtable { ["MARSHAL_SECRET_MOONRAKER_VORON"] = "from-env" }, _directory);

        string? value = await source.TryGetAsync(_voron, null, TestContext.Current.CancellationToken);

        Assert.Equal("from-env", value);
    }

    [Fact]
    public async Task TryGet_DockerSecretFile_TrimsTrailingNewline()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "moonraker_voron"), "from-file\n", TestContext.Current.CancellationToken);
        var source = new EnvironmentFileSecretSource(new Hashtable(), _directory);

        string? value = await source.TryGetAsync(_voron, null, TestContext.Current.CancellationToken);

        Assert.Equal("from-file", value);
    }

    [Fact]
    public async Task TryGet_EnvironmentBeforeFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "moonraker_voron"), "from-file", TestContext.Current.CancellationToken);
        var source = new EnvironmentFileSecretSource(new Hashtable { ["MARSHAL_SECRET_MOONRAKER_VORON"] = "from-env" }, _directory);

        Assert.Equal("from-env", await source.TryGetAsync(_voron, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TryGet_Missing_ReturnsNull()
    {
        var source = new EnvironmentFileSecretSource(new Hashtable(), Path.Combine(_directory, "does-not-exist"));

        Assert.Null(await source.TryGetAsync(_voron, null, TestContext.Current.CancellationToken));
        Assert.False(source.IsOwnerScoped);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
