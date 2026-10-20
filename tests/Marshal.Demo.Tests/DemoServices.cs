using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Notes;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Demo.Seeding;
using Marshal.Integrations.FreshRss;
using Marshal.Integrations.HomeAssistant;
using Marshal.Integrations.Mail;
using Marshal.Integrations.Moonraker;
using Marshal.Integrations.Notes;
using Marshal.Integrations.Web;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Demo.Tests;

/// <summary>
/// The integrations' real registrations plus demo mode on a service collection, with fakes for the core services
/// they need. Settings are <c>marshal.yaml</c> keys in configuration form (<c>ha:url</c>).
/// </summary>
internal sealed class DemoServices : IAsyncDisposable
{
    private DemoServices(ServiceProvider provider, FakeTimeProvider time, FakeEventPublisher events, FakeMcpServerCatalog mcp, RecordingMailProvider mail)
    {
        Mail = mail;
        Provider = provider;
        Time = time;
        Events = events;
        Mcp = mcp;
    }

    public ServiceProvider Provider { get; }

    public FakeTimeProvider Time { get; }

    public FakeEventPublisher Events { get; }

    public FakeMcpServerCatalog Mcp { get; }

    /// <summary>The stand-in for the IMAP provider; every call that reaches it is recorded.</summary>
    public RecordingMailProvider Mail { get; }

    public DemoWorld World => Provider.GetRequiredService<DemoWorld>();

    public static DemoServices Create(Dictionary<string, string?>? settings = null, bool demo = true, Action<IServiceCollection>? configure = null)
    {
        var values = new Dictionary<string, string?> { ["demo:enabled"] = demo ? "true" : "false" };
        foreach ((string key, string? value) in settings ?? [])
            values[key] = value;
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
        var events = new FakeEventPublisher();
        var mcp = new FakeMcpServerCatalog();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<IEventPublisher>(events);
        services.AddSingleton<IMcpServerCatalog>(mcp);
        services.AddSingleton<IUserPreferencesStore, FakeUserPreferencesStore>();
        services.AddSingleton<IUserProfileProvider, ViennaProfiles>();
        services.AddSingleton<ISpeakerDirectory>(new FakeSpeakerDirectory());
        services.AddSingleton<IExecutionContextAccessor>(new FakeExecutionContextAccessor(TestExecutionContext.Text()));
        services.AddSingleton<ISnapshotLinks, PathLinks>();
        services.AddSingleton<IModelRouter, NoModels>();
        services.AddSingleton<IQuarantinedExtractor, NoExtractor>();
        services.AddSingleton<ICostCalculator, ZeroCost>();
        services.AddSingleton<ITemplateCatalog>(new FakeTemplateCatalog());
        services.AddSingleton<IRunRecorder>(new InMemoryRunRecorder());
        services.AddSingleton<IEventSubscriber, NoSubscriptions>();
        services.AddSingleton<INotificationSender>(new FakeNotificationSender());
        services.AddSingleton<IInstanceOwner>(new FixedOwner());
        services.AddMarshalHomeAssistant(configuration);
        services.AddMarshalPrinters(configuration, _ => Path.Combine(Path.GetTempPath(), "marshal-demo-tests", "snapshots"));
        AddMail(services, configuration);
        services.AddMarshalFreshRss(configuration);
        services.AddMarshalWeb(configuration);
        services.AddMarshalNotes(configuration, _ => Path.Combine(Path.GetTempPath(), "marshal-demo-tests", "no-notes"));
        services.Replace(ServiceDescriptor.Singleton<INotesIndex, NoNotesIndex>());
        configure?.Invoke(services);
        services.AddMarshalDemo(configuration);
        services.Replace(ServiceDescriptor.Singleton<IDemoSeeder, NoSeeder>());
        return new DemoServices(services.BuildServiceProvider(), time, events, mcp, (RecordingMailProvider)services.Single(descriptor => descriptor.ServiceType == typeof(RecordingMailProvider)).ImplementationInstance!);
    }

    /// <summary>The real mail registration, with the IMAP provider and the database store swapped for in-memory fakes.</summary>
    private static void AddMail(ServiceCollection services, IConfiguration configuration)
    {
        var secrets = new FakeSecretStore();
        services.AddSingleton<IArtifactStore>(new FakeArtifactStore());
        services.AddSingleton<ISecretStore>(secrets);
        services.AddSingleton<ISecretWriter>(new FakeSecretWriter(secrets));
        services.AddSingleton<IUserDirectory>(new OneUser());
        services.AddMarshalMail(configuration);

        var provider = new RecordingMailProvider();
        services.AddSingleton(provider);
        services.Replace(ServiceDescriptor.Singleton<IMailProvider>(provider));
        services.Replace(ServiceDescriptor.Singleton<IMailAccountStore, InMemoryMailAccounts>());
    }

    public T Get<T>()
        where T : notnull => Provider.GetRequiredService<T>();

    /// <summary>Every tool of every registered source, by id.</summary>
    public async Task<Dictionary<string, ITool>> ToolsAsync()
    {
        var tools = new Dictionary<string, ITool>();
        foreach (IToolSource source in Provider.GetServices<IToolSource>())
        {
            foreach (ITool tool in await source.GetToolsAsync(TestContext.Current.CancellationToken))
                tools[tool.Descriptor.Id.ToString()] = tool;
        }

        return tools;
    }

    public static async Task<string> InvokeAsync(ITool tool, string arguments, MarshalExecutionContext? context = null)
    {
        Result<ToolOutput> result = await tool.InvokeAsync(new ToolInvocation(JsonDocument.Parse(arguments).RootElement.Clone(), context ?? TestExecutionContext.Text()), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value.Content;
    }

    public async ValueTask DisposeAsync() => await Provider.DisposeAsync();

    private sealed class PathLinks : ISnapshotLinks
    {
        public string For(string printer, string id) => $"/snapshots/{printer}/{id}";

        public string Shared(string printer, string id) => $"{For(printer, id)}/shared";
    }

    private sealed class NoExtractor : IQuarantinedExtractor
    {
        public Task<Result<ExtractionAnswer>> ExtractAsync(UntrustedItem item, string question, AnswerSpec answer, MarshalExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult<Result<ExtractionAnswer>>(new Error("model.not_available", "No model.", ErrorType.Unavailable));
    }

    private sealed class NoModels : IModelRouter
    {
        public Result<RoutedModel> Route(ModelRequest request) => new Error("model.not_available", "No model.", ErrorType.Unavailable);
    }

    private sealed class ZeroCost : ICostCalculator
    {
        public decimal Calculate(string modelKey, UsageDetails? usage) => 0;
    }

    private sealed class NoSubscriptions : IEventSubscriber
    {
        public IDisposable Subscribe(Func<MarshalEvent, CancellationToken, ValueTask> handler) => new CancellationTokenSource();
    }

    private sealed class FixedOwner : IInstanceOwner
    {
        public Task<OwnerId> GetAsync(CancellationToken cancellationToken) => Task.FromResult(TestExecutionContext.OwnerA);
    }

    private sealed class NoNotesIndex : INotesIndex
    {
        public Task<Result<NotesIndexUpdate>> UpdateAsync(INotesSource source, CancellationToken cancellationToken) =>
            Task.FromResult<Result<NotesIndexUpdate>>(new NotesIndexUpdate([], []));

        public Task<IReadOnlyList<NoteHit>> SearchAsync(string query, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NoteHit>>([]);
    }

    /// <summary>The real seeder needs the memory store and audit log; these tests cover the simulated integrations.</summary>
    private sealed class NoSeeder : IDemoSeeder
    {
        public Task<bool> IsSeededAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WasSeededBeforeAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<Result<int>> SeedAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult<Result<int>>(0);
    }

    private sealed class OneUser : IUserDirectory
    {
        public Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnerId>>([TestExecutionContext.OwnerA]);
    }

    private sealed class ViennaProfiles : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Alex", "en", "Europe/Vienna"));
    }
}
