using System.Net;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Executors;
using Marshal.Tasks.Executors;
using Marshal.Testing;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Tests.Executors;

public sealed class ClaudeCodeExecutorTests
{
    private static readonly Uri _callback = new("http://marshal.test/api/v1/executor/callbacks/abc");

    private readonly RecordingHandler _handler = new();
    private readonly FakeSecretStore _secrets = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ClaudeCodeExecutorTests() => _secrets.Values["executor_host_token"] = "host-token";

    [Theory]
    [InlineData("""{"repository":"demo","prompt":"Add a feature file."}""", true)]
    [InlineData("""{"repository":"demo","prompt":"Fix it.","baseBranch":"develop"}""", true)]
    [InlineData("""{"repository":"other","prompt":"Add a feature file."}""", false)]
    [InlineData("""{"repository":"demo","prompt":" "}""", false)]
    [InlineData("""{"repository":"demo","prompt":"x","shell":"rm -rf /"}""", false)]
    [InlineData("""["demo"]""", false)]
    public void ValidateSpec_OnlyConfiguredRepositoriesAndKnownFields(string spec, bool valid)
    {
        Result result = Executor().ValidateSpec(JsonDocument.Parse(spec).RootElement);

        Assert.Equal(valid, result.IsSuccess);
        if (!valid)
            Assert.Equal("executor.invalid_spec", result.Error.Code);
    }

    [Fact]
    public void Description_ListsTheRepositories()
    {
        Assert.Contains("demo (the demo app)", Executor().Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_PostsTheSessionWithHostTokenAndCallback()
    {
        _handler.Respond(HttpStatusCode.Accepted, """{"id":"s-1","branch":"marshal/s-1"}""");
        JsonElement spec = JsonDocument.Parse("""{"repository":"demo","prompt":"Add a feature file.","baseBranch":"develop"}""").RootElement;

        Result<ExecutorStarted> started = await Executor().StartAsync(new ExecutorStart(Guid.NewGuid(), spec, _callback, "callback-token"), Ct);

        Assert.Equal(new ExecutorStarted("s-1", "demo"), started.Value);
        (HttpMethod method, Uri url, string? authorization, string? body) = Assert.Single(_handler.Requests);
        Assert.Equal((HttpMethod.Post, new Uri("http://executor.test:8080/sessions"), "Bearer host-token"), (method, url, authorization));
        JsonElement sent = JsonDocument.Parse(body!).RootElement;
        Assert.Equal(("demo", "Add a feature file.", "develop", _callback.ToString(), "callback-token"),
            (sent.GetProperty("repository").GetString(), sent.GetProperty("prompt").GetString(), sent.GetProperty("baseBranch").GetString(),
             sent.GetProperty("callbackUrl").GetString(), sent.GetProperty("callbackToken").GetString()));
    }

    [Fact]
    public async Task Start_HostRefusesOrIsDown_StartFailed()
    {
        _handler.Respond(HttpStatusCode.BadRequest, """{"error":"Unknown repository."}""");
        JsonElement spec = JsonDocument.Parse("""{"repository":"demo","prompt":"x"}""").RootElement;

        Result<ExecutorStarted> refused = await Executor().StartAsync(new ExecutorStart(Guid.NewGuid(), spec, _callback, "t"), Ct);
        _handler.Throw = true;
        Result<ExecutorStarted> down = await Executor().StartAsync(new ExecutorStart(Guid.NewGuid(), spec, _callback, "t"), Ct);

        Assert.Equal(("executor.start_failed", "executor.start_failed"), (refused.Error.Code, down.Error.Code));
        Assert.Equal(ErrorType.Unavailable, down.Error.Type);
    }

    [Fact]
    public async Task Start_HostTokenSecretMissing_Unavailable()
    {
        _secrets.Values.Clear();
        JsonElement spec = JsonDocument.Parse("""{"repository":"demo","prompt":"x"}""").RootElement;

        Result<ExecutorStarted> started = await Executor().StartAsync(new ExecutorStart(Guid.NewGuid(), spec, _callback, "t"), Ct);

        Assert.Equal(ErrorType.Unavailable, started.Error.Type);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Cancel_DeletesTheSession()
    {
        _handler.Respond(HttpStatusCode.Accepted, "");

        Result cancelled = await Executor().CancelAsync("s-1", Ct);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal((HttpMethod.Delete, new Uri("http://executor.test:8080/sessions/s-1")), (_handler.Requests[0].Method, _handler.Requests[0].Url));
    }

    private ClaudeCodeExecutor Executor() => new(new SingleClientFactory(_handler), _secrets, Options.Create(new ExecutorOptions
    {
        ClaudeCode = new ClaudeCodeOptions
        {
            Url = new Uri("http://executor.test:8080/"),
            TokenSecret = "executor_host_token",
            Repositories = [new ExecutorRepository { Name = "demo", Description = "the demo app" }],
        },
    }));

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "";

        public List<(HttpMethod Method, Uri Url, string? Authorization, string? Body)> Requests { get; } = [];

        public bool Throw { get; set; }

        public void Respond(HttpStatusCode status, string body) => (_status, _body) = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw)
                throw new HttpRequestException("Connection refused.");
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
