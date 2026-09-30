using Marshal.ExecutorHost;
using Marshal.ExecutorHost.Callbacks;
using Marshal.ExecutorHost.Git;
using Marshal.ExecutorHost.Sandbox;
using Marshal.ExecutorHost.Sessions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<ExecutorHostOptions>().Bind(builder.Configuration.GetSection("executorHost"))
    .Validate(options => options.MarshalUrl is { IsAbsoluteUri: true }, "executorHost.marshalUrl must be Marshal's absolute URL.")
    .ValidateOnStart();
builder.Services.AddHttpClient(HttpCallbackSender.ClientName);
builder.Services.AddSingleton<ICallbackSender, HttpCallbackSender>();
builder.Services.AddSingleton<ISandboxRuntime, DockerCliRuntime>();
builder.Services.AddSingleton<IWorktrees, GitWorktrees>();
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<HostToken>();

WebApplication app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

RouteGroupBuilder sessions = app.MapGroup("/sessions").AddEndpointFilter<HostToken>();
sessions.MapPost("/", async (SessionRequest request, SessionManager manager, CancellationToken ct) =>
{
    (StartedSession? started, string? error) = await manager.StartAsync(request, ct);
    return started is null ? Results.BadRequest(new { error }) : Results.Accepted($"/sessions/{started.Id}", started);
});
sessions.MapDelete("/{id}", (string id, SessionManager manager) => manager.Cancel(id) ? Results.Accepted() : Results.NotFound());
sessions.MapPost("/{id}/merge", async (string id, BranchRequest request, SessionManager manager, CancellationToken ct) =>
    BranchResponse(await manager.MergeAsync(id, request.Repository, ct), Results.Ok(new { merged = true })));
sessions.MapPost("/{id}/discard", async (string id, BranchRequest request, SessionManager manager, CancellationToken ct) =>
    BranchResponse(await manager.DiscardAsync(id, request.Repository, ct), Results.NoContent()));

app.Run();

static IResult BranchResponse(BranchResult result, IResult done) => result switch
{
    BranchResult.Done => done,
    BranchResult.NotFound => Results.NotFound(),
    BranchResult.InvalidRepository => Results.BadRequest(new { error = "Unknown repository." }),
    BranchResult.Running => Results.Conflict(new { error = "running" }),
    _ => Results.Conflict(new { error = "conflict" }),
};
