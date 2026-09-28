using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Secrets;
using Marshal.Core.Secrets;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Core.Tests.Secrets;

public sealed class SecretStoreTests
{
    private static readonly SecretName _freshRss = SecretName.Create("freshrss").Value;
    private static readonly SecretName _haToken = SecretName.Create("ha_token").Value;

    [Fact]
    public async Task GetForOwner_UserRowOverridesInstanceRow()
    {
        FakeDatabaseSource database = new FakeDatabaseSource().With(null, _freshRss, "instance").With(OwnerA, _freshRss, "owner-a");
        var store = new SecretStore([new FakeEnvironmentSource(), database]);

        Result<string> value = await store.GetForOwnerAsync(_freshRss, OwnerA, TestContext.Current.CancellationToken);

        Assert.Equal("owner-a", value.Value);
    }

    [Fact]
    public async Task GetForOwner_NoUserRow_FallsBackToInstanceRow()
    {
        FakeDatabaseSource database = new FakeDatabaseSource().With(null, _freshRss, "instance").With(OwnerB, _freshRss, "owner-b");
        var store = new SecretStore([database]);

        Result<string> value = await store.GetForOwnerAsync(_freshRss, OwnerA, TestContext.Current.CancellationToken);

        Assert.Equal("instance", value.Value);
    }

    [Fact]
    public async Task GetForOwner_EnvironmentWinsOverDatabase()
    {
        FakeEnvironmentSource environment = new FakeEnvironmentSource().With(_freshRss, "env");
        FakeDatabaseSource database = new FakeDatabaseSource().With(OwnerA, _freshRss, "owner-a");
        var store = new SecretStore([database, environment]);

        Result<string> value = await store.GetForOwnerAsync(_freshRss, OwnerA, TestContext.Current.CancellationToken);

        Assert.Equal("env", value.Value);
    }

    [Fact]
    public async Task Get_ConfigReference_NeverReadsUserRows()
    {
        FakeDatabaseSource database = new FakeDatabaseSource().With(OwnerA, _haToken, "owner-a");
        var store = new SecretStore([new FakeEnvironmentSource(), database]);

        Result<string> value = await store.GetAsync(_haToken, TestContext.Current.CancellationToken);

        Assert.Equal("secret.not_found", value.Error.Code);
    }

    [Fact]
    public async Task Get_ConfigReference_EnvironmentThenInstanceRow()
    {
        FakeDatabaseSource database = new FakeDatabaseSource().With(null, _haToken, "instance");
        var store = new SecretStore([database, new FakeEnvironmentSource()]);

        Result<string> fromDatabase = await store.GetAsync(_haToken, TestContext.Current.CancellationToken);
        var withEnvironment = new SecretStore([database, new FakeEnvironmentSource().With(_haToken, "env")]);
        Result<string> fromEnvironment = await withEnvironment.GetAsync(_haToken, TestContext.Current.CancellationToken);

        Assert.Equal("instance", fromDatabase.Value);
        Assert.Equal("env", fromEnvironment.Value);
    }

    [Fact]
    public async Task Get_Unknown_NotFoundWithoutValue()
    {
        var store = new SecretStore([new FakeEnvironmentSource(), new FakeDatabaseSource()]);

        Result<string> value = await store.GetAsync(_freshRss, TestContext.Current.CancellationToken);

        Assert.True(value.IsFailure);
        Assert.Equal("secret.not_found", value.Error.Code);
        Assert.Equal(ErrorType.NotFound, value.Error.Type);
        Assert.Contains("freshrss", value.Error.Message, StringComparison.Ordinal);
    }

    private sealed class FakeEnvironmentSource : ISecretSource
    {
        private readonly Dictionary<string, string> _values = [];

        public bool IsOwnerScoped => false;

        public FakeEnvironmentSource With(SecretName name, string value)
        {
            _values[name.Value] = value;
            return this;
        }

        public Task<string?> TryGetAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken) =>
            Task.FromResult(_values.GetValueOrDefault(name.Value));
    }

    private sealed class FakeDatabaseSource : ISecretSource
    {
        private readonly Dictionary<(OwnerId?, string), string> _values = [];

        public bool IsOwnerScoped => true;

        public FakeDatabaseSource With(OwnerId? owner, SecretName name, string value)
        {
            _values[(owner, name.Value)] = value;
            return this;
        }

        public Task<string?> TryGetAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken) =>
            Task.FromResult(_values.GetValueOrDefault((owner, name.Value)));
    }
}
